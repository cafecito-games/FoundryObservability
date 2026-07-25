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

private func exceptionPayload(_ value: Any?) -> FoundryExceptionPayload? {
    let dictionary = dictionaryValue(value)
    guard !dictionary.isEmpty else {
        return nil
    }
    return FoundryExceptionPayload(
        typeName: stringValue(dictionary["type_name"]),
        message: stringValue(dictionary["message"]),
        stackTrace: stringValue(dictionary["stack_trace"]),
        attributes: dictionaryValue(dictionary["attributes"])
    )
}

@Foundry
class SentryObservabilityBridge: RefCounted {
    private var globalAttributes: [String: Any] = [:]
    private var configured = false
    private var logsEnabled = false
    private var metricsEnabled = false
    private var didShutdown = false

    @Callable
    func configure(payload: VariantDictionary) -> Int {
        let values = foundationDictionary(from: payload)
        let enabled = boolValue(values["enabled"])

        closeActiveClient()
        didShutdown = false
        globalAttributes = dictionaryValue(values["global_attributes"])
        logsEnabled = boolValue(values["logs_enabled"])
        metricsEnabled = boolValue(values["metrics_enabled"])

        guard enabled else {
            return bridgeErrorOK
        }

        let dsn = stringValue(values["dsn"])
        guard !dsn.isEmpty else {
            return bridgeErrorFailed
        }

        let options = Options()
        options.dsn = dsn
        guard options.dsn != nil else {
            return bridgeErrorFailed
        }

        let environment = stringValue(values["environment"])
        let release = stringValue(values["release"])
        let dist = stringValue(values["dist"])
        if !environment.isEmpty {
            options.environment = environment
        }
        if !release.isEmpty {
            options.releaseName = release
        }
        if !dist.isEmpty {
            options.dist = dist
        }
        options.debug = boolValue(dictionaryValue(values["provider_options"])["debug"])
        options.sendDefaultPii = boolValue(dictionaryValue(values["provider_options"])["send_default_pii"])
        options.enableLogs = logsEnabled
        options.enableMetrics = metricsEnabled
        options.enabled = true

        SentrySDK.start(options: options)
        configured = true
        return bridgeErrorOK
    }

    @Callable
    func isAvailable() -> Bool {
        configured && !didShutdown && SentrySDK.isEnabled
    }

    @Callable
    func capture(payload: VariantDictionary) -> String {
        guard isAvailable() else {
            return ""
        }

        let values = foundationDictionary(from: payload)
        let exception = exceptionPayload(values["exception"])
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
        let eventID = SentrySDK.capture(event: event)
        let eventIDString = eventID.sentryIdString
        return eventIDString == SentryId.empty.sentryIdString ? "" : eventIDString
    }

    @Callable
    func captureLog(payload: VariantDictionary) -> String {
        guard isAvailable(), logsEnabled else {
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
        guard isAvailable() else {
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
        guard isAvailable(), metricsEnabled else {
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
        guard isAvailable() else {
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
    func flush(_ timeoutMsec: Int) -> Int {
        guard isAvailable() else {
            return bridgeErrorFailed
        }
        SentrySDK.flush(timeout: sentryTimeoutSeconds(milliseconds: timeoutMsec))
        return bridgeErrorOK
    }

    @Callable
    func shutdown() {
        guard !didShutdown else {
            return
        }
        didShutdown = true
        closeActiveClient()
    }

    private func closeActiveClient() {
        if configured {
            SentrySDK.close()
        }
        configured = false
        logsEnabled = false
        metricsEnabled = false
    }
}

private func optionalStringValue(_ value: Any?) -> String? {
    let string = stringValue(value)
    return string.isEmpty ? nil : string
}
