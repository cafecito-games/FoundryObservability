import Sentry
import XCTest
@testable import FoundryObservabilitySentry

final class SentryLifecycleCoordinatorTests: XCTestCase {
    func testPublishesOwnerOnlyAfterSuccessfulStart() {
        let driver = FakeSentryLifecycleDriver()
        let coordinator = SentryLifecycleCoordinator(driver: driver)

        XCTAssertTrue(coordinator.configure(owner: "first", configuration: configuration()))
        XCTAssertEqual(coordinator.activeOwner, "first")
        XCTAssertTrue(coordinator.isAvailable(owner: "first"))
        XCTAssertEqual(driver.operations, ["start:1.0.0"])

        driver.failNextStart = true
        let freshCoordinator = SentryLifecycleCoordinator(driver: driver)
        XCTAssertFalse(freshCoordinator.configure(
            owner: "failed",
            configuration: configuration(release: "2.0.0")
        ))
        XCTAssertNil(freshCoordinator.activeOwner)
    }

    func testEquivalentConfigurationTransfersOwnershipWithoutRestart() {
        let driver = FakeSentryLifecycleDriver()
        let coordinator = SentryLifecycleCoordinator(driver: driver)
        let config = configuration()

        XCTAssertTrue(coordinator.configure(owner: "first", configuration: config))
        XCTAssertTrue(coordinator.configure(owner: "second", configuration: config))

        XCTAssertEqual(driver.operations, ["start:1.0.0"])
        XCTAssertEqual(coordinator.activeOwner, "second")
        XCTAssertFalse(coordinator.isAvailable(owner: "first"))
        XCTAssertTrue(coordinator.isAvailable(owner: "second"))
    }

    func testChangedMaxBreadcrumbsClosesThenStarts() {
        let driver = FakeSentryLifecycleDriver()
        let coordinator = SentryLifecycleCoordinator(driver: driver)

        XCTAssertTrue(coordinator.configure(
            owner: "first",
            configuration: configuration(maxBreadcrumbs: 100)
        ))
        XCTAssertTrue(coordinator.configure(
            owner: "second",
            configuration: configuration(maxBreadcrumbs: 2)
        ))

        XCTAssertEqual(
            driver.operations,
            ["start:1.0.0", "close", "start:1.0.0"]
        )
    }

    func testChangedConfigurationClosesThenStarts() {
        let driver = FakeSentryLifecycleDriver()
        let coordinator = SentryLifecycleCoordinator(driver: driver)

        XCTAssertTrue(coordinator.configure(owner: "first", configuration: configuration()))
        XCTAssertTrue(coordinator.configure(
            owner: "second",
            configuration: configuration(release: "2.0.0")
        ))

        XCTAssertEqual(
            driver.operations,
            ["start:1.0.0", "close", "start:2.0.0"]
        )
        XCTAssertEqual(coordinator.activeOwner, "second")
    }

    func testChangedStableContextsCloseThenStart() {
        let driver = FakeSentryLifecycleDriver()
        let coordinator = SentryLifecycleCoordinator(driver: driver)

        XCTAssertTrue(coordinator.configure(
            owner: "first",
            configuration: configuration(
                stableContexts: ["foundry_engine": ["version": "4.5"]]
            )
        ))
        XCTAssertTrue(coordinator.configure(
            owner: "second",
            configuration: configuration(
                stableContexts: ["foundry_engine": ["version": "4.6"]]
            )
        ))

        XCTAssertEqual(
            driver.operations,
            ["start:1.0.0", "close", "start:1.0.0"]
        )
        XCTAssertEqual(coordinator.activeOwner, "second")
    }

    func testStaleOwnerCannotFlushOrShutdown() {
        let driver = FakeSentryLifecycleDriver()
        let coordinator = SentryLifecycleCoordinator(driver: driver)
        let config = configuration()

        XCTAssertTrue(coordinator.configure(owner: "first", configuration: config))
        XCTAssertTrue(coordinator.configure(owner: "second", configuration: config))

        XCTAssertFalse(coordinator.flush(owner: "first", timeout: 0.25))
        coordinator.shutdown(owner: "first")
        XCTAssertEqual(driver.operations, ["start:1.0.0"])
        XCTAssertTrue(coordinator.isAvailable(owner: "second"))

        XCTAssertTrue(coordinator.flush(owner: "second", timeout: 0.25))
        XCTAssertEqual(driver.operations, ["start:1.0.0", "flush:0.25"])
    }

    func testAvailableOwnerCanPerformGuardedBreadcrumbClear() {
        let driver = FakeSentryLifecycleDriver()
        let coordinator = SentryLifecycleCoordinator(driver: driver)
        let scope = Scope()
        scope.addBreadcrumb(Breadcrumb())

        XCTAssertTrue(coordinator.configure(owner: "first", configuration: configuration()))
        XCTAssertFalse(coordinator.perform(owner: "stale") {
            scope.clearBreadcrumbs()
        })
        XCTAssertNotNil(scope.serialize()["breadcrumbs"])
        XCTAssertTrue(coordinator.perform(owner: "first") {
            scope.clearBreadcrumbs()
        })

        XCTAssertNil(scope.serialize()["breadcrumbs"])
    }

    func testScopeKeysSurviveOwnerTransferAndResetAtSessionBoundary() {
        let driver = FakeSentryLifecycleDriver()
        let coordinator = SentryLifecycleCoordinator(driver: driver)
        let config = configuration()

        XCTAssertTrue(coordinator.configure(owner: "first", configuration: config))
        XCTAssertTrue(coordinator.replaceScope(owner: "first") { previousKeys in
            XCTAssertTrue(previousKeys.tagKeys.isEmpty)
            return FoundryInstalledScopeKeys(tagKeys: ["region"])
        })
        XCTAssertFalse(coordinator.replaceScope(owner: "first") { _ in nil })
        XCTAssertTrue(coordinator.configure(owner: "second", configuration: config))
        XCTAssertTrue(coordinator.replaceScope(owner: "second") { previousKeys in
            XCTAssertEqual(previousKeys.tagKeys, ["region"])
            return FoundryInstalledScopeKeys()
        })

        XCTAssertTrue(coordinator.replaceScope(owner: "second") { _ in
            FoundryInstalledScopeKeys(contextKeys: ["match"])
        })
        XCTAssertTrue(coordinator.configure(
            owner: "third",
            configuration: configuration(release: "2.0.0")
        ))
        XCTAssertTrue(coordinator.replaceScope(owner: "third") { previousKeys in
            XCTAssertTrue(previousKeys.contextKeys.isEmpty)
            return FoundryInstalledScopeKeys()
        })
    }

    func testFailedReplacementRestoresPreviousOwnerAndConfiguration() {
        let driver = FakeSentryLifecycleDriver()
        let coordinator = SentryLifecycleCoordinator(driver: driver)

        XCTAssertTrue(coordinator.configure(owner: "first", configuration: configuration()))
        driver.failNextStart = true

        XCTAssertFalse(coordinator.configure(
            owner: "second",
            configuration: configuration(release: "2.0.0")
        ))

        XCTAssertEqual(
            driver.operations,
            ["start:1.0.0", "close", "start:2.0.0", "start:1.0.0"]
        )
        XCTAssertEqual(coordinator.activeOwner, "first")
        XCTAssertTrue(coordinator.isAvailable(owner: "first"))
        XCTAssertFalse(coordinator.isAvailable(owner: "second"))
    }

    func testShutdownIsIdempotent() {
        let driver = FakeSentryLifecycleDriver()
        let coordinator = SentryLifecycleCoordinator(driver: driver)

        XCTAssertTrue(coordinator.configure(owner: "first", configuration: configuration()))
        coordinator.shutdown(owner: "first")
        coordinator.shutdown(owner: "first")

        XCTAssertEqual(driver.operations, ["start:1.0.0", "close"])
        XCTAssertNil(coordinator.activeOwner)
    }

    func testAppleOptionsEnableCrashHandlerAndStableMetadata() {
        let config = configuration(
            release: "game@1.2.3",
            environment: "qa",
            dist: "macos",
            globalAttributes: ["build": 42],
            stableContexts: [
                "foundry_engine": ["version": "4.5", "debug_build": true],
            ]
        )

        let options = makeAppleSentryOptions(config)

        XCTAssertTrue(options.enableCrashHandler)
        XCTAssertEqual(options.shutdownTimeInterval, 2.0)
        XCTAssertEqual(options.releaseName, "game@1.2.3")
        XCTAssertEqual(options.environment, "qa")
        XCTAssertEqual(options.dist, "macos")
        XCTAssertEqual(options.maxBreadcrumbs, 2)
        XCTAssertEqual(
            foundryCrashContext(config) as NSDictionary,
            ["global_attributes": ["build": 42]] as NSDictionary
        )
        let initialScope = options.initialScope(Scope())
        let contexts = initialScope.serialize()["context"] as? [String: Any]
        XCTAssertEqual(
            contexts?["foundry"] as? NSDictionary,
            ["global_attributes": ["build": 42]] as NSDictionary
        )
        XCTAssertEqual(
            contexts?["foundry_engine"] as? NSDictionary,
            ["version": "4.5", "debug_build": true] as NSDictionary
        )
    }

    func testAppleOptionsClampNegativeMaxBreadcrumbsToZero() {
        let options = makeAppleSentryOptions(configuration(maxBreadcrumbs: -5))

        XCTAssertEqual(options.maxBreadcrumbs, 0)
    }

    private func configuration(
        release: String = "1.0.0",
        environment: String = "test",
        dist: String = "macos",
        globalAttributes: [String: Any] = [:],
        stableContexts: [String: Any] = [:],
        maxBreadcrumbs: Int = 2
    ) -> SentryLifecycleConfiguration {
        SentryLifecycleConfiguration(
            dsn: "https://public@example.com/1",
            environment: environment,
            release: release,
            dist: dist,
            globalAttributes: globalAttributes,
            stableContexts: stableContexts,
            providerOptions: [:],
            logsEnabled: true,
            metricsEnabled: true,
            applicationHangDetectionEnabled: true,
            applicationHangTimeoutMsec: 3_200,
            maxBreadcrumbs: maxBreadcrumbs
        )
    }
}

private final class FakeSentryLifecycleDriver: SentryLifecycleDriving {
    var isEnabled = false
    var failNextStart = false
    var operations: [String] = []

    func start(configuration: SentryLifecycleConfiguration) -> Bool {
        operations.append("start:\(configuration.release)")
        if failNextStart {
            failNextStart = false
            isEnabled = false
            return false
        }
        isEnabled = true
        return true
    }

    func flush(timeout: TimeInterval) {
        operations.append("flush:\(timeout)")
    }

    func close() {
        operations.append("close")
        isEnabled = false
    }
}
