import Foundation
import Sentry

func applyAppleHangDiagnostics(
    from values: [String: Any],
    to options: Options
) {
    if let enabled = values["application_hang_detection_enabled"] as? Bool {
        options.enableAppHangTracking = enabled
    }
    if let timeoutMsec = diagnosticInteger(
        values["application_hang_timeout_msec"]
    ) {
        options.appHangTimeoutInterval = Double(timeoutMsec) / 1000.0
    }
}

private func diagnosticInteger(_ value: Any?) -> Int? {
    if let value = value as? Int {
        return value
    }
    if let value = value as? Int64 {
        return Int(exactly: value)
    }
    if let value = value as? Double {
        return Int(exactly: value)
    }
    return nil
}
