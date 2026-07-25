import Sentry
import XCTest
@testable import FoundryObservabilitySentry

final class SentryDiagnosticsConfiguratorTests: XCTestCase {
    func testAppliesAppleHangOptionsFromPayload() {
        let options = Options()

        applyAppleHangDiagnostics(
            from: [
                "application_hang_detection_enabled": false,
                "application_hang_timeout_msec": Int64(3_200),
            ],
            to: options
        )

        XCTAssertFalse(options.enableAppHangTracking)
        XCTAssertEqual(options.appHangTimeoutInterval, 3.2, accuracy: 0.000_001)
    }

    func testAppliesTrueAppleHangOptionFromPayload() {
        let options = Options()
        options.enableAppHangTracking = false

        applyAppleHangDiagnostics(
            from: ["application_hang_detection_enabled": true],
            to: options
        )

        XCTAssertTrue(options.enableAppHangTracking)
    }

    func testInvalidDoubleTimeoutsPreserveExistingTimeout() {
        for timeoutMsec in [
            Double.nan,
            Double.infinity,
            Double.greatestFiniteMagnitude,
            3_200.5,
        ] {
            let options = Options()
            options.appHangTimeoutInterval = 7.3

            applyAppleHangDiagnostics(
                from: ["application_hang_timeout_msec": timeoutMsec],
                to: options
            )

            XCTAssertEqual(options.appHangTimeoutInterval, 7.3, accuracy: 0.000_001)
        }
    }

    func testBelowMinimumTimeoutsPreserveExistingTimeout() {
        for timeoutMsec in [0, -1, 999] {
            let options = Options()
            options.appHangTimeoutInterval = 7.3

            applyAppleHangDiagnostics(
                from: ["application_hang_timeout_msec": timeoutMsec],
                to: options
            )

            XCTAssertEqual(options.appHangTimeoutInterval, 7.3, accuracy: 0.000_001)
        }
    }

    func testMissingPayloadKeysPreserveNativeDefaults() {
        let options = Options()
        let originalEnabled = options.enableAppHangTracking
        let originalTimeout = options.appHangTimeoutInterval

        applyAppleHangDiagnostics(from: [:], to: options)

        XCTAssertEqual(options.enableAppHangTracking, originalEnabled)
        XCTAssertEqual(options.appHangTimeoutInterval, originalTimeout)
    }
}
