import Foundation
@preconcurrency import Sentry

let sentryLifecycleVersion = 1
let sentryShutdownTimeoutSeconds: TimeInterval = 2.0

struct SentryLifecycleConfiguration: Equatable {
    let dsn: String
    let environment: String
    let release: String
    let dist: String
    let globalAttributes: [String: Any]
    let stableContexts: [String: Any]
    let providerOptions: [String: Any]
    let logsEnabled: Bool
    let metricsEnabled: Bool
    let applicationHangDetectionEnabled: Bool
    let applicationHangTimeoutMsec: Int
    let maxBreadcrumbs: Int

    static func == (
        lhs: SentryLifecycleConfiguration,
        rhs: SentryLifecycleConfiguration
    ) -> Bool {
        lhs.dsn == rhs.dsn
            && lhs.environment == rhs.environment
            && lhs.release == rhs.release
            && lhs.dist == rhs.dist
            && NSDictionary(dictionary: lhs.globalAttributes)
                .isEqual(to: rhs.globalAttributes)
            && NSDictionary(dictionary: lhs.stableContexts)
                .isEqual(to: rhs.stableContexts)
            && NSDictionary(dictionary: lhs.providerOptions)
                .isEqual(to: rhs.providerOptions)
            && lhs.logsEnabled == rhs.logsEnabled
            && lhs.metricsEnabled == rhs.metricsEnabled
            && lhs.applicationHangDetectionEnabled
                == rhs.applicationHangDetectionEnabled
            && lhs.applicationHangTimeoutMsec
                == rhs.applicationHangTimeoutMsec
            && lhs.maxBreadcrumbs == rhs.maxBreadcrumbs
    }
}

protocol SentryLifecycleDriving: AnyObject {
    var isEnabled: Bool { get }

    func start(configuration: SentryLifecycleConfiguration) -> Bool
    func flush(timeout: TimeInterval)
    func close()
}

final class SentryLifecycleCoordinator: @unchecked Sendable {
    private let driver: SentryLifecycleDriving
    private let lock = NSLock()
    private var owner: String?
    private var configuration: SentryLifecycleConfiguration?
    private var installedScopeKeys = FoundryInstalledScopeKeys()

    init(driver: SentryLifecycleDriving) {
        self.driver = driver
    }

    var activeOwner: String? {
        withLock { owner }
    }

    var activeConfiguration: SentryLifecycleConfiguration? {
        withLock { configuration }
    }

    func configure(
        owner candidateOwner: String,
        configuration candidateConfiguration: SentryLifecycleConfiguration
    ) -> Bool {
        guard !candidateOwner.isEmpty, !candidateConfiguration.dsn.isEmpty else {
            return false
        }

        return withLock {
            if configuration == candidateConfiguration, driver.isEnabled {
                owner = candidateOwner
                return true
            }

            let previousOwner = owner
            let previousConfiguration = configuration
            installedScopeKeys = FoundryInstalledScopeKeys()
            if driver.isEnabled {
                driver.close()
            }
            owner = nil
            configuration = nil

            if driver.start(configuration: candidateConfiguration) {
                owner = candidateOwner
                configuration = candidateConfiguration
                return true
            }

            if let previousOwner, let previousConfiguration,
                driver.start(configuration: previousConfiguration)
            {
                owner = previousOwner
                configuration = previousConfiguration
            }
            return false
        }
    }

    func isAvailable(owner candidateOwner: String) -> Bool {
        withLock {
            !candidateOwner.isEmpty
                && candidateOwner == owner
                && driver.isEnabled
        }
    }

    @discardableResult
    func perform(owner candidateOwner: String, operation: () -> Void) -> Bool {
        withLock {
            guard
                !candidateOwner.isEmpty,
                candidateOwner == owner,
                driver.isEnabled
            else {
                return false
            }
            operation()
            return true
        }
    }

    @discardableResult
    func replaceScope(
        owner candidateOwner: String,
        operation: (FoundryInstalledScopeKeys) -> FoundryInstalledScopeKeys?
    ) -> Bool {
        withLock {
            guard
                !candidateOwner.isEmpty,
                candidateOwner == owner,
                driver.isEnabled,
                let nextKeys = operation(installedScopeKeys)
            else {
                return false
            }
            installedScopeKeys = nextKeys
            return true
        }
    }

    @discardableResult
    func flush(owner candidateOwner: String, timeout: TimeInterval) -> Bool {
        withLock {
            guard
                !candidateOwner.isEmpty,
                candidateOwner == owner,
                driver.isEnabled
            else {
                return false
            }
            driver.flush(timeout: timeout)
            return true
        }
    }

    func shutdown(owner candidateOwner: String) {
        withLock {
            guard !candidateOwner.isEmpty, candidateOwner == owner else {
                return
            }
            if driver.isEnabled {
                driver.close()
            }
            owner = nil
            configuration = nil
            installedScopeKeys = FoundryInstalledScopeKeys()
        }
    }

    private func withLock<T>(_ operation: () -> T) -> T {
        lock.lock()
        defer { lock.unlock() }
        return operation()
    }
}

final class AppleSentrySDKDriver: SentryLifecycleDriving {
    var isEnabled: Bool {
        SentrySDK.isEnabled
    }

    func start(configuration: SentryLifecycleConfiguration) -> Bool {
        let options = makeAppleSentryOptions(configuration)
        guard options.dsn != nil else {
            return false
        }
        SentrySDK.start(options: options)
        return SentrySDK.isEnabled
    }

    func flush(timeout: TimeInterval) {
        SentrySDK.flush(timeout: timeout)
    }

    func close() {
        SentrySDK.close()
    }
}

func makeAppleSentryOptions(
    _ configuration: SentryLifecycleConfiguration
) -> Options {
    let options = Options()
    options.dsn = configuration.dsn
    options.enableCrashHandler = true
    options.shutdownTimeInterval = sentryShutdownTimeoutSeconds
    options.enabled = true
    if !configuration.environment.isEmpty {
        options.environment = configuration.environment
    }
    if !configuration.release.isEmpty {
        options.releaseName = configuration.release
    }
    if !configuration.dist.isEmpty {
        options.dist = configuration.dist
    }
    options.debug = configuration.providerOptions["debug"] as? Bool ?? false
    options.sendDefaultPii =
        configuration.providerOptions["send_default_pii"] as? Bool ?? false
    options.enableLogs = configuration.logsEnabled
    options.enableMetrics = configuration.metricsEnabled
    options.maxBreadcrumbs = UInt(max(0, configuration.maxBreadcrumbs))
    applyAppleHangDiagnostics(
        from: [
            "application_hang_detection_enabled":
                configuration.applicationHangDetectionEnabled,
            "application_hang_timeout_msec":
                configuration.applicationHangTimeoutMsec,
        ],
        to: options
    )
    options.initialScope = { scope in
        scope.setContext(
            value: foundryCrashContext(configuration),
            key: "foundry"
        )
        applySentryContexts(
            foundrySentryContexts(configuration.stableContexts),
            to: scope
        )
        return scope
    }
    return options
}

func foundryCrashContext(
    _ configuration: SentryLifecycleConfiguration
) -> [String: Any] {
    ["global_attributes": configuration.globalAttributes]
}
