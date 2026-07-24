import Foundation
import Sentry

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
