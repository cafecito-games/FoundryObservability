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
            globalAttributes: ["build": 42]
        )

        let options = makeAppleSentryOptions(config)

        XCTAssertTrue(options.enableCrashHandler)
        XCTAssertEqual(options.shutdownTimeInterval, 2.0)
        XCTAssertEqual(options.releaseName, "game@1.2.3")
        XCTAssertEqual(options.environment, "qa")
        XCTAssertEqual(options.dist, "macos")
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
    }

    private func configuration(
        release: String = "1.0.0",
        environment: String = "test",
        dist: String = "macos",
        globalAttributes: [String: Any] = [:]
    ) -> SentryLifecycleConfiguration {
        SentryLifecycleConfiguration(
            dsn: "https://public@example.com/1",
            environment: environment,
            release: release,
            dist: dist,
            globalAttributes: globalAttributes,
            providerOptions: [:],
            logsEnabled: true,
            metricsEnabled: true,
            applicationHangDetectionEnabled: true,
            applicationHangTimeoutMsec: 3_200
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
