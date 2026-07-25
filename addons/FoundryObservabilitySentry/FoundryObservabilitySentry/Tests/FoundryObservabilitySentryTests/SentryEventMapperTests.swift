import Foundation
import Sentry
import XCTest
@testable import FoundryObservabilitySentry

final class SentryEventMapperTests: XCTestCase {
    func testMapsLevels() {
        XCTAssertEqual(sentryLevel(for: 10), .debug)
        XCTAssertEqual(sentryLevel(for: 20), .debug)
        XCTAssertEqual(sentryLevel(for: 30), .info)
        XCTAssertEqual(sentryLevel(for: 40), .warning)
        XCTAssertEqual(sentryLevel(for: 50), .error)
        XCTAssertEqual(sentryLevel(for: 60), .fatal)
        XCTAssertEqual(sentryLevel(for: 35), .error)
    }

    func testMapsStructuredLogLevels() {
        XCTAssertEqual(sentryLogLevel(for: 10), .trace)
        XCTAssertEqual(sentryLogLevel(for: 20), .debug)
        XCTAssertEqual(sentryLogLevel(for: 30), .info)
        XCTAssertEqual(sentryLogLevel(for: 40), .warn)
        XCTAssertEqual(sentryLogLevel(for: 50), .error)
        XCTAssertEqual(sentryLogLevel(for: 60), .fatal)
        XCTAssertEqual(sentryLogLevel(for: 999), .error)
    }

    func testEventExtrasOverrideGlobalExtrasAndPreserveMetadata() {
        let result = mergedExtras(
            global: ["shared": "global", "build": 42],
            event: ["shared": "event"],
            kind: "log",
            source: "combat",
            timestampMsec: 1_612_325_106_123,
            engineTicksMsec: 4567
        )

        XCTAssertEqual(result["shared"] as? String, "event")
        XCTAssertEqual(result["build"] as? Int, 42)
        XCTAssertEqual(result["foundry.kind"] as? String, "log")
        XCTAssertEqual(result["foundry.source"] as? String, "combat")
        XCTAssertEqual(result["foundry.timestamp_msec"] as? Int64, 1_612_325_106_123)
        XCTAssertEqual(result["foundry.engine_ticks_msec"] as? Int64, 4567)
    }

    func testStructuredLogAttributesPreserveFieldsAndReservedMetadata() {
        let attributes = mergedLogAttributes(
            global: ["shared": "global", "build": 42],
            event: ["shared": "event", "foundry.kind": "caller"],
            kind: "log",
            source: "foundry.logging",
            timestampMsec: 1_612_325_106_123,
            engineTicksMsec: 4567
        )

        XCTAssertEqual(attributes["shared"] as? String, "event")
        XCTAssertEqual(attributes["build"] as? Int, 42)
        XCTAssertEqual(attributes["foundry.kind"] as? String, "log")
        XCTAssertEqual(attributes["foundry.source"] as? String, "foundry.logging")
        XCTAssertEqual(attributes["foundry.timestamp_msec"] as? Int64, 1_612_325_106_123)
        XCTAssertEqual(attributes["foundry.engine_ticks_msec"] as? Int64, 4567)
    }

    func testUnavailableEngineTicksRemoveCallerControlledReservedMetadata() {
        let callerAttributes = ["foundry.engine_ticks_msec": 999]
        let extras = mergedExtras(
            global: callerAttributes,
            event: callerAttributes,
            kind: "message",
            source: "game",
            timestampMsec: 1_612_325_106_123,
            engineTicksMsec: -1
        )
        let logAttributes = mergedLogAttributes(
            global: callerAttributes,
            event: callerAttributes,
            kind: "log",
            source: "game",
            timestampMsec: 1_612_325_106_123,
            engineTicksMsec: -1
        )

        XCTAssertNil(extras["foundry.engine_ticks_msec"])
        XCTAssertNil(logAttributes["foundry.engine_ticks_msec"])
    }

    func testStructuredLogAttributesOmitUnsupportedValues() {
        let attributes = scalarLogAttributes([
            "supported": "yes",
            "unsupported": ["nested": true],
            "timestamp": Int64(1234)
        ])

        XCTAssertEqual(attributes["supported"] as? String, "yes")
        XCTAssertEqual(attributes["timestamp"] as? Int, 1234)
        XCTAssertNil(attributes["unsupported"])
    }

    func testBreadcrumbDataPreservesFieldsAndReservedMetadata() {
        let data = sentryBreadcrumbData(
            global: ["build": 42, "error.file": "global"],
            breadcrumb: ["error.file": "res://player.fs"],
            timestampMsec: 1234
        )

        XCTAssertEqual(data["build"] as? Int, 42)
        XCTAssertEqual(data["error.file"] as? String, "res://player.fs")
        XCTAssertEqual(data["foundry.timestamp_msec"] as? Int64, 1234)
    }

    func testBuildsBreadcrumbWithWallClockTimestampAndEngineTickData() {
        let wallClock = Date(timeIntervalSince1970: 1_700_000_000)
        let breadcrumb = makeSentryBreadcrumb(
            message: "warning",
            level: 40,
            category: "error",
            timestampMsec: 1234,
            sdkTimestamp: wallClock,
            globalAttributes: ["build": 42],
            breadcrumbAttributes: ["error.file": "res://player.fs"]
        )

        XCTAssertEqual(breadcrumb.message, "warning")
        XCTAssertEqual(breadcrumb.category, "error")
        XCTAssertEqual(breadcrumb.level, .warning)
        XCTAssertEqual(breadcrumb.timestamp, wallClock)
        XCTAssertEqual(breadcrumb.data?["build"] as? Int, 42)
        XCTAssertEqual(breadcrumb.data?["error.file"] as? String, "res://player.fs")
        XCTAssertEqual(breadcrumb.data?["foundry.timestamp_msec"] as? Int64, 1234)
    }

    func testMetricAttributesPreserveSupportedScalarValues() {
        let attributes = sentryMetricAttributes([
            "string": "value",
            "bool": true,
            "int": 42,
            "int64": Int64(43),
            "float": Float(1.5),
            "double": 2.5,
            "nested": ["unsupported": true],
        ])

        XCTAssertEqual(attributes["string"]?.asSentryAttributeContent, .string("value"))
        XCTAssertEqual(attributes["bool"]?.asSentryAttributeContent, .boolean(true))
        XCTAssertEqual(attributes["int"]?.asSentryAttributeContent, .integer(42))
        XCTAssertEqual(attributes["int64"]?.asSentryAttributeContent, .integer(43))
        XCTAssertEqual(attributes["float"]?.asSentryAttributeContent, .double(1.5))
        XCTAssertEqual(attributes["double"]?.asSentryAttributeContent, .double(2.5))
        XCTAssertNil(attributes["nested"])
    }

    func testMetricUnitsMapKnownAndCustomValues() {
        XCTAssertNil(sentryMetricUnit(for: ""))
        XCTAssertEqual(sentryMetricUnit(for: "millisecond"), .millisecond)
        XCTAssertEqual(sentryMetricUnit(for: "player"), .generic("player"))
    }

    func testConvertsTimeoutMillisecondsToSeconds() {
        XCTAssertEqual(sentryTimeoutSeconds(milliseconds: 321), 0.321, accuracy: 0.0001)
    }

    func testConvertsUnixMillisecondsWithoutTimezoneDependence() {
        let timestampMsec: Int64 = 1_612_325_106_123
        let original = NSTimeZone.default
        NSTimeZone.default = TimeZone(secondsFromGMT: 9 * 3_600)!
        defer { NSTimeZone.default = original }

        let date = sentryDate(timestampMsec: timestampMsec)

        XCTAssertEqual(date.timeIntervalSince1970, 1_612_325_106.123, accuracy: 0.000_001)
    }

    func testRejectsInvalidFeedbackAssociatedEventID() {
        XCTAssertNil(sentryFeedbackAssociatedEventID(for: "not-a-sentry-id"))
        XCTAssertNil(sentryFeedbackAssociatedEventID(for: "event-123"))
        XCTAssertNotNil(
            sentryFeedbackAssociatedEventID(for: "0123456789abcdef0123456789abcdef")
        )
    }

    func testBuildsExceptionEventWithMappedMetadata() {
        let event = makeSentryEvent(
            message: "boom",
            level: 50,
            source: "combat",
            kind: "exception",
            timestampMsec: 1_612_325_106_123,
            engineTicksMsec: 4567,
            globalAttributes: ["release_channel": "beta"],
            eventAttributes: ["attempt": 2],
            exception: FoundryExceptionPayload(
                typeName: "InvalidState",
                message: "bad state",
                stackTrace: "at Player.attack()",
                attributes: ["entity": "player"]
            )
        )

        XCTAssertEqual(event.message?.formatted, "boom")
        XCTAssertEqual(event.level, .error)
        XCTAssertEqual(event.logger, "combat")
        guard let eventTimestamp = event.timestamp else {
            XCTFail("Expected mapped event timestamp")
            return
        }
        XCTAssertEqual(
            eventTimestamp.timeIntervalSince1970,
            1_612_325_106.123,
            accuracy: 0.000_001
        )
        XCTAssertEqual(event.extra?["release_channel"] as? String, "beta")
        XCTAssertEqual(event.extra?["attempt"] as? Int, 2)
        XCTAssertEqual(event.extra?["foundry.timestamp_msec"] as? Int64, 1_612_325_106_123)
        XCTAssertEqual(event.extra?["foundry.engine_ticks_msec"] as? Int64, 4567)
        XCTAssertEqual(event.extra?["foundry.exception_type"] as? String, "InvalidState")
        XCTAssertEqual(event.extra?["foundry.stack_trace"] as? String, "at Player.attack()")
        XCTAssertEqual(event.exceptions?.first?.type, "InvalidState")
        XCTAssertEqual(event.exceptions?.first?.value, "bad state")
    }
}
