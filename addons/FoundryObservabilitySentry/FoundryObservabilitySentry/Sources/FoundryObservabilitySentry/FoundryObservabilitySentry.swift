import Foundation
import FoundrySwift
@preconcurrency import Sentry

#initFoundryExtension(
    cdecl: "foundry_observability_sentry_entry_point",
    types: [SentryObservabilityBridge.self]
)

private let bridgeErrorOK = 0
private let bridgeErrorFailed = 1

private func foundationDictionary(from dictionary: VariantDictionary) -> [String: Any] {
    var result: [String: Any] = [:]
    let keys = dictionary.keys()
    for index in 0..<Int(keys.size()) {
        guard
            let keyVariant = keys[index],
            let key = String(keyVariant),
            let valueVariant = dictionary.get(key: keyVariant, default: nil),
            let value = foundationValue(from: valueVariant)
        else {
            continue
        }
        result[key] = value
    }
    return result
}

private func foundationValue(from variant: Variant) -> Any? {
    if let value = Bool(variant) {
        return value
    }
    if let value = Int64(variant) {
        return value
    }
    if let value = Double(variant) {
        return value
    }
    if let value = String(variant) {
        return value
    }
    if let dictionary = VariantDictionary(variant) {
        return foundationDictionary(from: dictionary)
    }
    if let array = VariantArray(variant) {
        var result: [Any] = []
        for index in 0..<Int(array.size()) {
            guard let valueVariant = array[index], let value = foundationValue(from: valueVariant) else {
                continue
            }
            result.append(value)
        }
        return result
    }
    return nil
}

private func stringValue(_ value: Any?) -> String {
    value as? String ?? ""
}

private func intValue(_ value: Any?) -> Int {
    if let value = value as? Int {
        return value
    }
    if let value = value as? Int64 {
        return Int(value)
    }
    if let value = value as? Double {
        return Int(value)
    }
    return 0
}

private func doubleValue(_ value: Any?) -> Double? {
    if let value = value as? Double {
        return value
    }
    if let value = value as? Int64 {
        return Double(value)
    }
    if let value = value as? Int {
        return Double(value)
    }
    return nil
}

private func boolValue(_ value: Any?) -> Bool {
    value as? Bool ?? false
}

private func dictionaryValue(_ value: Any?) -> [String: Any] {
    value as? [String: Any] ?? [:]
}

@Foundry
class SentryObservabilityBridge: RefCounted {
    private static let lifecycleCoordinator = SentryLifecycleCoordinator(
        driver: AppleSentrySDKDriver()
    )

    private var globalAttributes: [String: Any] = [:]
    private var lifecycleOwner = ""
    private var logsEnabled = false
    private var metricsEnabled = false

    @Callable
    func lifecycleVersion() -> Int {
        sentryLifecycleVersion
    }

    @Callable
    func configure(payload: VariantDictionary) -> Int {
        let values = foundationDictionary(from: payload)
        let enabled = boolValue(values["enabled"])
        let candidateOwner = stringValue(values["lifecycle_owner"])
        guard !candidateOwner.isEmpty else {
            return bridgeErrorFailed
        }

        if !enabled {
            Self.lifecycleCoordinator.shutdown(owner: candidateOwner)
            if candidateOwner == lifecycleOwner {
                lifecycleOwner = ""
                globalAttributes = [:]
                logsEnabled = false
                metricsEnabled = false
            }
            return bridgeErrorOK
        }

        let dsn = stringValue(values["dsn"])
        guard !dsn.isEmpty else {
            return bridgeErrorFailed
        }

        let candidateConfiguration = SentryLifecycleConfiguration(
            dsn: dsn,
            environment: stringValue(values["environment"]),
            release: stringValue(values["release"]),
            dist: stringValue(values["dist"]),
            globalAttributes: dictionaryValue(values["global_attributes"]),
            stableContexts: dictionaryValue(values["stable_contexts"]),
            providerOptions: dictionaryValue(values["provider_options"]),
            logsEnabled: boolValue(values["logs_enabled"]),
            metricsEnabled: boolValue(values["metrics_enabled"]),
            applicationHangDetectionEnabled:
                boolValue(values["application_hang_detection_enabled"]),
            applicationHangTimeoutMsec:
                intValue(values["application_hang_timeout_msec"])
        )
        guard Self.lifecycleCoordinator.configure(
            owner: candidateOwner,
            configuration: candidateConfiguration
        ) else {
            return bridgeErrorFailed
        }

        lifecycleOwner = candidateOwner
        globalAttributes = candidateConfiguration.globalAttributes
        logsEnabled = candidateConfiguration.logsEnabled
        metricsEnabled = candidateConfiguration.metricsEnabled
        return bridgeErrorOK
    }

    @Callable
    func isAvailable(_ owner: String) -> Bool {
        Self.lifecycleCoordinator.isAvailable(owner: owner)
    }

    @Callable
    func capture(payload: VariantDictionary) -> String {
        guard isAvailable(lifecycleOwner) else {
            return ""
        }

        let values = foundationDictionary(from: payload)
        let exception = foundryExceptionPayload(values["exception"])
        let event = makeSentryEvent(
            message: stringValue(values["message"]),
            level: intValue(values["level"]),
            source: stringValue(values["source"]),
            kind: stringValue(values["kind"]),
            timestampMsec: Int64(intValue(values["timestamp_msec"])),
            engineTicksMsec: Int64(intValue(values["engine_ticks_msec"])),
            globalAttributes: globalAttributes,
            eventAttributes: dictionaryValue(values["attributes"]),
            exception: exception
        )
        let contexts = foundrySentryContexts(values["contexts"])
        let eventID = SentrySDK.capture(event: event) { scope in
            applySentryContexts(contexts, to: scope)
        }
        let eventIDString = eventID.sentryIdString
        return eventIDString == SentryId.empty.sentryIdString ? "" : eventIDString
    }

    @Callable
    func captureLog(payload: VariantDictionary) -> String {
        guard isAvailable(lifecycleOwner), logsEnabled else {
            return ""
        }

        let values = foundationDictionary(from: payload)
        let attributes = scalarLogAttributes(mergedLogAttributes(
            global: globalAttributes,
            event: dictionaryValue(values["attributes"]),
            kind: stringValue(values["kind"]),
            source: stringValue(values["source"]),
            timestampMsec: Int64(intValue(values["timestamp_msec"])),
            engineTicksMsec: Int64(intValue(values["engine_ticks_msec"]))
        ))
        let message = stringValue(values["message"])
        switch sentryLogLevel(for: intValue(values["level"])) {
        case .trace:
            SentrySDK.logger.trace(message, attributes: attributes)
        case .debug:
            SentrySDK.logger.debug(message, attributes: attributes)
        case .info:
            SentrySDK.logger.info(message, attributes: attributes)
        case .warn:
            SentrySDK.logger.warn(message, attributes: attributes)
        case .error:
            SentrySDK.logger.error(message, attributes: attributes)
        case .fatal:
            SentrySDK.logger.fatal(message, attributes: attributes)
        @unknown default:
            SentrySDK.logger.error(message, attributes: attributes)
        }
        return "sentry-log:\(UUID().uuidString)"
    }

    @Callable
    func captureBreadcrumb(payload: VariantDictionary) -> Bool {
        guard isAvailable(lifecycleOwner) else {
            return false
        }

        let values = foundationDictionary(from: payload)
        let timestampMsec = Int64(intValue(values["timestamp_msec"]))
        let breadcrumb = makeSentryBreadcrumb(
            message: stringValue(values["message"]),
            level: intValue(values["level"]),
            category: stringValue(values["category"]),
            timestampMsec: timestampMsec,
            globalAttributes: globalAttributes,
            breadcrumbAttributes: dictionaryValue(values["attributes"])
        )
        SentrySDK.addBreadcrumb(breadcrumb)
        return true
    }

    @Callable
    func captureMetric(payload: VariantDictionary) -> Bool {
        guard isAvailable(lifecycleOwner), metricsEnabled else {
            return false
        }

        let values = foundationDictionary(from: payload)
        let type = intValue(values["type"])
        let name = stringValue(values["name"])
        guard
            (0...2).contains(type),
            !name.isEmpty,
            let value = doubleValue(values["value"]),
            value.isFinite
        else {
            return false
        }

        let attributes = sentryMetricAttributes(dictionaryValue(values["attributes"]))
        let unit = sentryMetricUnit(for: stringValue(values["unit"]))
        switch type {
        case 0:
            guard
                value >= 0,
                value.rounded(.towardZero) == value,
                value <= Double(UInt.max),
                let count = UInt(exactly: value)
            else {
                return false
            }
            SentrySDK.metrics.count(key: name, value: count, attributes: attributes)
        case 1:
            SentrySDK.metrics.gauge(
                key: name,
                value: value,
                unit: unit,
                attributes: attributes
            )
        case 2:
            SentrySDK.metrics.distribution(
                key: name,
                value: value,
                unit: unit,
                attributes: attributes
            )
        default:
            return false
        }
        return true
    }

    @Callable
    func captureFeedback(payload: VariantDictionary) -> String {
        guard isAvailable(lifecycleOwner) else {
            return ""
        }

        let values = foundationDictionary(from: payload)
        let message = stringValue(values["message"])
        guard !message.isEmpty else {
            return ""
        }

        let name = optionalStringValue(values["name"])
        let email = optionalStringValue(values["contact_email"])
        let associatedEventIDValue = stringValue(values["associated_event_id"])
        let associatedEventID = sentryFeedbackAssociatedEventID(for: associatedEventIDValue)
        if !associatedEventIDValue.isEmpty && associatedEventID == nil {
            return ""
        }

        let feedback = SentryFeedback(
            message: message,
            name: name,
            email: email,
            source: .custom,
            associatedEventId: associatedEventID,
            attachments: nil
        )
        SentrySDK.capture(feedback: feedback)
        return "sentry-feedback:\(UUID().uuidString)"
    }

    @Callable
    func flush(_ owner: String, _ timeoutMsec: Int) -> Int {
        // Stale owners are idempotent no-ops. The public provider also gates
        // this call with isAvailable before reaching the bridge.
        _ = Self.lifecycleCoordinator.flush(
            owner: owner,
            timeout: sentryTimeoutSeconds(milliseconds: timeoutMsec)
        )
        return bridgeErrorOK
    }

    @Callable
    func shutdown(_ owner: String) {
        Self.lifecycleCoordinator.shutdown(owner: owner)
        if owner == lifecycleOwner {
            lifecycleOwner = ""
            globalAttributes = [:]
            logsEnabled = false
            metricsEnabled = false
        }
    }
}

private func optionalStringValue(_ value: Any?) -> String? {
    let string = stringValue(value)
    return string.isEmpty ? nil : string
}
