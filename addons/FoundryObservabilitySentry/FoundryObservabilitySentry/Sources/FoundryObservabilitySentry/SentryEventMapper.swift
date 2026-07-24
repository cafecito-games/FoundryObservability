import Foundation
import Sentry

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
    timestampMsec: Int64
) -> [String: Any] {
    var attributes = global
    for (key, value) in event {
        attributes[key] = value
    }
    attributes["foundry.kind"] = kind
    attributes["foundry.source"] = source
    attributes["foundry.timestamp_msec"] = timestampMsec
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

struct FoundryExceptionPayload {
    let typeName: String
    let message: String
    let stackTrace: String
    let attributes: [String: Any]
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

func mergedExtras(
    global: [String: Any],
    event: [String: Any],
    kind: String,
    source: String,
    timestampMsec: Int64,
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
    return extras
}

func makeSentryEvent(
    message: String,
    level: Int,
    source: String,
    kind: String,
    timestampMsec: Int64,
    globalAttributes: [String: Any] = [:],
    eventAttributes: [String: Any] = [:],
    exception: FoundryExceptionPayload? = nil
) -> Event {
    let event = Event()
    event.message = SentryMessage(formatted: message)
    event.level = sentryLevel(for: level)
    event.logger = source.isEmpty ? nil : source
    event.extra = mergedExtras(
        global: globalAttributes,
        event: eventAttributes,
        kind: kind,
        source: source,
        timestampMsec: timestampMsec,
        exception: exception
    )

    if let exception {
        event.exceptions = [Exception(value: exception.message, type: exception.typeName)]
    }
    return event
}
