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

    func testEventExtrasOverrideGlobalExtrasAndPreserveMetadata() {
        let result = mergedExtras(
            global: ["shared": "global", "build": 42],
            event: ["shared": "event"],
            kind: "log",
            source: "combat",
            timestampMsec: 1234
        )

        XCTAssertEqual(result["shared"] as? String, "event")
        XCTAssertEqual(result["build"] as? Int, 42)
        XCTAssertEqual(result["foundry.kind"] as? String, "log")
        XCTAssertEqual(result["foundry.source"] as? String, "combat")
        XCTAssertEqual(result["foundry.timestamp_msec"] as? Int64, 1234)
    }

    func testConvertsTimeoutMillisecondsToSeconds() {
        XCTAssertEqual(sentryTimeoutSeconds(milliseconds: 321), 0.321, accuracy: 0.0001)
    }

    func testBuildsExceptionEventWithMappedMetadata() {
        let event = makeSentryEvent(
            message: "boom",
            level: 50,
            source: "combat",
            kind: "exception",
            timestampMsec: 1234,
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
        XCTAssertEqual(event.extra?["release_channel"] as? String, "beta")
        XCTAssertEqual(event.extra?["attempt"] as? Int, 2)
        XCTAssertEqual(event.extra?["foundry.exception_type"] as? String, "InvalidState")
        XCTAssertEqual(event.extra?["foundry.stack_trace"] as? String, "at Player.attack()")
        XCTAssertEqual(event.exceptions?.first?.type, "InvalidState")
        XCTAssertEqual(event.exceptions?.first?.value, "bad state")
    }
}
