import Foundation
import FoundrySwift
import Sentry

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
    private var didShutdown = false

    @Callable
    func configure(payload: VariantDictionary) -> Int {
        let values = foundationDictionary(from: payload)
        let enabled = boolValue(values["enabled"])

        closeActiveClient()
        didShutdown = false
        globalAttributes = dictionaryValue(values["global_attributes"])

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
            globalAttributes: globalAttributes,
            eventAttributes: dictionaryValue(values["attributes"]),
            exception: exception
        )
        let eventID = SentrySDK.capture(event: event)
        let eventIDString = eventID.sentryIdString
        return eventIDString == SentryId.empty.sentryIdString ? "" : eventIDString
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
    }
}
