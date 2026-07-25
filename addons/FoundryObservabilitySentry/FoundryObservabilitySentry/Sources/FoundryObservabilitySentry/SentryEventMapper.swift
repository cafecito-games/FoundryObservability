import Foundation
import Sentry

private let foundryVariableMaxContainerDepth = 8
private let foundryVariableMaxItemCount = 256

func sentryLogLevel(for level: Int) -> SentryLog.Level {
    switch level {
    case 10:
        return .trace
    case 20:
        return .debug
    case 30:
        return .info
    case 40:
        return .warn
    case 50:
        return .error
    case 60:
        return .fatal
    default:
        return .error
    }
}

func mergedLogAttributes(
    global: [String: Any],
    event: [String: Any],
    kind: String,
    source: String,
    timestampMsec: Int64,
    engineTicksMsec: Int64
) -> [String: Any] {
    var attributes = global
    for (key, value) in event {
        attributes[key] = value
    }
    attributes["foundry.kind"] = kind
    attributes["foundry.source"] = source
    attributes["foundry.timestamp_msec"] = timestampMsec
    if engineTicksMsec >= 0 {
        attributes["foundry.engine_ticks_msec"] = engineTicksMsec
    } else {
        attributes.removeValue(forKey: "foundry.engine_ticks_msec")
    }
    return attributes
}

func scalarLogAttributes(_ attributes: [String: Any]) -> [String: Any] {
    var result: [String: Any] = [:]
    for (key, value) in attributes {
        switch value {
        case let value as String:
            result[key] = value
        case let value as Bool:
            result[key] = value
        case let value as Int:
            result[key] = value
        case let value as Int64:
            result[key] = Int(value)
        case let value as Double:
            result[key] = value
        case let value as Float:
            result[key] = Double(value)
        default:
            continue
        }
    }
    return result
}

func sentryBreadcrumbData(
    global: [String: Any],
    breadcrumb: [String: Any],
    timestampMsec: Int64
) -> [String: Any] {
    var data = global
    for (key, value) in breadcrumb {
        data[key] = value
    }
    data["foundry.timestamp_msec"] = timestampMsec
    return data
}

func makeSentryBreadcrumb(
    message: String,
    level: Int,
    category: String,
    timestampMsec: Int64,
    sdkTimestamp: Date = Date(),
    globalAttributes: [String: Any] = [:],
    breadcrumbAttributes: [String: Any] = [:]
) -> Breadcrumb {
    let breadcrumb = Breadcrumb()
    breadcrumb.message = message
    breadcrumb.category = category
    breadcrumb.level = sentryLevel(for: level)
    breadcrumb.timestamp = sdkTimestamp
    breadcrumb.data = sentryBreadcrumbData(
        global: globalAttributes,
        breadcrumb: breadcrumbAttributes,
        timestampMsec: timestampMsec
    )
    return breadcrumb
}

func sentryMetricAttributes(_ attributes: [String: Any]) -> [String: SentryAttributeValue] {
    var result: [String: SentryAttributeValue] = [:]
    for (key, value) in attributes {
        switch value {
        case let value as String:
            result[key] = value
        case let value as Bool:
            result[key] = value
        case let value as Int:
            result[key] = value
        case let value as Int64:
            result[key] = Int(value)
        case let value as Float:
            result[key] = Double(value)
        case let value as Double:
            result[key] = value
        default:
            continue
        }
    }
    return result
}

func sentryMetricUnit(for value: String) -> SentryUnit? {
    value.isEmpty ? nil : SentryUnit(rawValue: value)
}

struct FoundryExceptionPayload {
    let typeName: String
    let message: String
    let stackTrace: String
    let attributes: [String: Any]
    let frames: [FoundryStackFramePayload]

    init(
        typeName: String,
        message: String,
        stackTrace: String,
        attributes: [String: Any],
        frames: [FoundryStackFramePayload] = []
    ) {
        self.typeName = typeName
        self.message = message
        self.stackTrace = stackTrace
        self.attributes = attributes
        self.frames = frames
    }
}

struct FoundryStackFramePayload {
    let file: String?
    let function: String?
    let line: Int?
    let language: String?
    let inApp: Bool
    let contextLine: String?
    let preContext: [String]?
    let postContext: [String]?
    let variables: [String: Any]?

    var isUseful: Bool {
        file != nil || function != nil || line != nil || language != nil
    }
}

func foundryExceptionPayload(_ value: Any?) -> FoundryExceptionPayload? {
    guard let dictionary = value as? [String: Any], !dictionary.isEmpty else {
        return nil
    }
    let frames = foundryStackFramePayloads(dictionary["frames"])
    return FoundryExceptionPayload(
        typeName: foundryString(dictionary["type_name"]) ?? "",
        message: foundryString(dictionary["message"]) ?? "",
        stackTrace: foundryString(dictionary["stack_trace"]) ?? "",
        attributes: foundryDictionary(dictionary["attributes"]) ?? [:],
        frames: frames
    )
}

private func foundryStackFramePayloads(_ value: Any?) -> [FoundryStackFramePayload] {
    guard let values = value as? [Any] else {
        return []
    }
    return values.compactMap { value in
        guard let dictionary = foundryDictionary(value) else {
            return nil
        }
        let contextLine = foundryNonEmptyString(dictionary["context_line"])
        let preContext = contextLine == nil ? nil : foundryStringArray(dictionary["pre_context"])
        let postContext = contextLine == nil ? nil : foundryStringArray(dictionary["post_context"])
        let variables = foundrySanitizedVariables(dictionary["variables"])
        let frame = FoundryStackFramePayload(
            file: foundryNonEmptyString(dictionary["file"]),
            function: foundryNonEmptyString(dictionary["function"]),
            line: foundryPositiveInteger(dictionary["line"]),
            language: foundryNonEmptyString(dictionary["language"]),
            inApp: foundryBool(dictionary["in_app"]),
            contextLine: contextLine,
            preContext: preContext,
            postContext: postContext,
            variables: variables
        )
        return frame.isUseful ? frame : nil
    }
}

private func foundryString(_ value: Any?) -> String? {
    value as? String
}

private func foundryNonEmptyString(_ value: Any?) -> String? {
    guard let value = foundryString(value), !value.isEmpty else {
        return nil
    }
    return value
}

private func foundryDictionary(_ value: Any?) -> [String: Any]? {
    value as? [String: Any]
}

private func foundrySanitizedVariables(_ value: Any?) -> [String: Any]? {
    guard
        let value,
        let dictionary = foundrySanitizedVariableDictionary(
            value,
            depth: 0,
            state: FoundryVariableCopyState()
        ),
        !dictionary.isEmpty
    else {
        return nil
    }
    return dictionary
}

private func foundrySanitizedVariableDictionary(
    _ value: Any,
    depth: Int,
    state: FoundryVariableCopyState
) -> [String: Any]? {
    guard
        depth <= foundryVariableMaxContainerDepth,
        let dictionary = value as? NSDictionary,
        state.visit(dictionary)
    else {
        return nil
    }

    var result: [String: Any] = [:]
    for (rawKey, rawValue) in dictionary {
        guard state.consumeItem() else {
            break
        }
        guard
            let key = rawKey as? String,
            let copied = foundrySanitizedVariableValue(
                rawValue,
                parentDepth: depth,
                state: state
            )
        else {
            continue
        }
        result[key] = copied
    }
    return result
}

private func foundrySanitizedVariableArray(
    _ value: Any,
    depth: Int,
    state: FoundryVariableCopyState
) -> [Any]? {
    guard
        depth <= foundryVariableMaxContainerDepth,
        let array = value as? NSArray,
        state.visit(array)
    else {
        return nil
    }

    var result: [Any] = []
    for rawValue in array {
        guard state.consumeItem() else {
            break
        }
        if let copied = foundrySanitizedVariableValue(
            rawValue,
            parentDepth: depth,
            state: state
        ) {
            result.append(copied)
        }
    }
    return result
}

private func foundrySanitizedVariableValue(
    _ value: Any,
    parentDepth: Int,
    state: FoundryVariableCopyState
) -> Any? {
    if let value = value as? String {
        return value
    }
    if let number = value as? NSNumber {
        let typeID = CFGetTypeID(number)
        if typeID == CFBooleanGetTypeID() {
            return number.boolValue
        }
        guard typeID == CFNumberGetTypeID(), number.doubleValue.isFinite else {
            return nil
        }
        return number
    }
    if value is NSDictionary {
        return foundrySanitizedVariableDictionary(
            value,
            depth: parentDepth + 1,
            state: state
        )
    }
    if value is NSArray {
        return foundrySanitizedVariableArray(
            value,
            depth: parentDepth + 1,
            state: state
        )
    }
    return nil
}

private final class FoundryVariableCopyState {
    private var visited: Set<ObjectIdentifier> = []
    private var itemCount = 0

    func visit(_ container: AnyObject) -> Bool {
        visited.insert(ObjectIdentifier(container)).inserted
    }

    func consumeItem() -> Bool {
        guard itemCount < foundryVariableMaxItemCount else {
            return false
        }
        itemCount += 1
        return true
    }
}

private func foundryStringArray(_ value: Any?) -> [String]? {
    guard let values = value as? [Any] else {
        return nil
    }
    let strings = values.compactMap { $0 as? String }
    return strings.isEmpty ? nil : strings
}

private func foundryPositiveInteger(_ value: Any?) -> Int? {
    if let number = value as? NSNumber, CFGetTypeID(number) == CFBooleanGetTypeID() {
        return nil
    }
    switch value {
    case let value as Int:
        return value > 0 ? value : nil
    case let value as Int64:
        guard value > 0, let integer = Int(exactly: value) else {
            return nil
        }
        return integer
    case let value as Double:
        guard
            value.isFinite,
            value > 0,
            value.rounded(.towardZero) == value,
            let integer = Int(exactly: value)
        else {
            return nil
        }
        return integer
    default:
        return nil
    }
}

private func foundryBool(_ value: Any?) -> Bool {
    guard
        let value = value as? NSNumber,
        CFGetTypeID(value) == CFBooleanGetTypeID()
    else {
        return true
    }
    return value.boolValue
}

func sentryLevel(for level: Int) -> SentryLevel {
    switch level {
    case 10, 20:
        return .debug
    case 30:
        return .info
    case 40:
        return .warning
    case 60:
        return .fatal
    case 50:
        return .error
    default:
        return .error
    }
}

func sentryTimeoutSeconds(milliseconds: Int) -> TimeInterval {
    TimeInterval(milliseconds) / 1000.0
}

func sentryDate(timestampMsec: Int64) -> Date {
    Date(timeIntervalSince1970: TimeInterval(timestampMsec) / 1_000.0)
}

func sentryFeedbackAssociatedEventID(for value: String) -> SentryId? {
    if value.isEmpty {
        return nil
    }
    let parsed = SentryId(uuidString: value)
    return parsed.sentryIdString == SentryId.empty.sentryIdString ? nil : parsed
}

func mergedExtras(
    global: [String: Any],
    event: [String: Any],
    kind: String,
    source: String,
    timestampMsec: Int64,
    engineTicksMsec: Int64,
    exception: FoundryExceptionPayload? = nil
) -> [String: Any] {
    var extras = global
    for (key, value) in event {
        extras[key] = value
    }

    if let exception {
        for (key, value) in exception.attributes {
            extras[key] = value
        }
        extras["foundry.exception_type"] = exception.typeName
        if !exception.stackTrace.isEmpty {
            extras["foundry.stack_trace"] = exception.stackTrace
        }
    }

    extras["foundry.kind"] = kind
    extras["foundry.source"] = source
    extras["foundry.timestamp_msec"] = timestampMsec
    if engineTicksMsec >= 0 {
        extras["foundry.engine_ticks_msec"] = engineTicksMsec
    } else {
        extras.removeValue(forKey: "foundry.engine_ticks_msec")
    }
    return extras
}

func makeSentryEvent(
    message: String,
    level: Int,
    source: String,
    kind: String,
    timestampMsec: Int64,
    engineTicksMsec: Int64,
    globalAttributes: [String: Any] = [:],
    eventAttributes: [String: Any] = [:],
    exception: FoundryExceptionPayload? = nil
) -> Event {
    let event = Event()
    event.message = SentryMessage(formatted: message)
    event.level = sentryLevel(for: level)
    event.logger = source.isEmpty ? nil : source
    event.timestamp = sentryDate(timestampMsec: timestampMsec)
    event.extra = mergedExtras(
        global: globalAttributes,
        event: eventAttributes,
        kind: kind,
        source: source,
        timestampMsec: timestampMsec,
        engineTicksMsec: engineTicksMsec,
        exception: exception
    )

    if let exception {
        let sentryException = Exception(value: exception.message, type: exception.typeName)
        if !exception.frames.isEmpty {
            let frames = exception.frames.map { payload in
                let frame = Frame()
                frame.fileName = payload.file
                frame.function = payload.function
                frame.lineNumber = payload.line.map { NSNumber(value: $0) }
                frame.platform = payload.language
                frame.inApp = NSNumber(value: payload.inApp)
                frame.contextLine = payload.contextLine
                frame.preContext = payload.preContext
                frame.postContext = payload.postContext
                frame.vars = payload.variables
                return frame
            }
            sentryException.stacktrace = SentryStacktrace(frames: frames, registers: [:])
        }
        event.exceptions = [sentryException]
    }
    return event
}
