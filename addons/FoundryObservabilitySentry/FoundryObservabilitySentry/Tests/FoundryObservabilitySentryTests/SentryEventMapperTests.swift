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
            type: "http",
            timestampMsec: 1234,
            sdkTimestamp: wallClock,
            globalAttributes: ["build": 42],
            breadcrumbAttributes: ["error.file": "res://player.fs"]
        )

        XCTAssertEqual(breadcrumb.message, "warning")
        XCTAssertEqual(breadcrumb.category, "error")
        XCTAssertEqual(breadcrumb.type, "http")
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

    func testSanitizesRuntimeContexts() {
        let cycle = NSMutableDictionary()
        cycle["self"] = cycle
        let contexts = foundrySentryContexts([
            "foundry_engine": [
                "version": "4.5",
                "debug_build": true,
                "nested": [
                    "kept": 1,
                    "infinite": Double.infinity,
                ],
                "unsupported": NSObject(),
                "cycle": cycle,
            ],
            "": ["invalid": true],
            "empty": [:],
        ])

        XCTAssertEqual(contexts["foundry_engine"]?["version"] as? String, "4.5")
        XCTAssertEqual(contexts["foundry_engine"]?["debug_build"] as? Bool, true)
        let nested = contexts["foundry_engine"]?["nested"] as? [String: Any]
        XCTAssertEqual(nested?["kept"] as? Int, 1)
        XCTAssertNil(nested?["infinite"])
        XCTAssertNil(contexts["foundry_engine"]?["unsupported"])
        XCTAssertNil((contexts["foundry_engine"]?["cycle"] as? [String: Any])?["self"])
        XCTAssertNil(contexts[""])
        XCTAssertNil(contexts["empty"])
    }

    func testAppliesRuntimeContextsToScope() {
        let contexts = foundrySentryContexts([
            "foundry_engine": ["version": "4.5"],
        ])
        let scope = Scope()

        applySentryContexts(contexts, to: scope)

        let serialized = scope.serialize()["context"] as? [String: Any]
        XCTAssertEqual(
            (serialized?["foundry_engine"] as? [String: Any])?["version"] as? String,
            "4.5"
        )
    }

    func testMapsRepresentativeFoundryScopePayloadWithoutAdditionalPII() {
        let payload = foundryScopePayload([
            "tags": ["region": "iad", "invalid": 42],
            "contexts": [
                "match": [
                    "id": "match-9",
                    "teams": [
                        ["name": "red", "score": 2],
                        ["name": "blue", "score": 1],
                    ],
                ],
            ],
            "user": [
                "id": "player-7",
                "display_name": "Mina",
                "contact_email": "mina@example.com",
                "ip_address": "203.0.113.1",
            ],
        ])

        XCTAssertEqual(payload.tags, ["region": "iad"])
        XCTAssertEqual(payload.user?["id"], "player-7")
        XCTAssertEqual(payload.user?["display_name"], "Mina")
        XCTAssertEqual(payload.user?["contact_email"], "mina@example.com")
        XCTAssertNil(payload.user?["ip_address"])
        let teams = payload.contexts["match"]?["teams"] as? [[String: Any]]
        XCTAssertEqual(teams?[0]["name"] as? String, "red")
        XCTAssertEqual(teams?[1]["score"] as? Int, 1)

        let scope = Scope()
        applyFoundryScope(payload, to: scope)
        let serialized = scope.serialize()
        XCTAssertEqual(
            (serialized["tags"] as? [String: String])?["region"],
            "iad"
        )
        XCTAssertEqual(
            ((serialized["context"] as? [String: Any])?["match"] as? [String: Any])?["id"]
                as? String,
            "match-9"
        )
        let user = serialized["user"] as? [String: Any]
        XCTAssertEqual(user?["id"] as? String, "player-7")
        XCTAssertEqual(user?["username"] as? String, "Mina")
        XCTAssertEqual(user?["email"] as? String, "mina@example.com")
        XCTAssertNil(user?["ip_address"])
        XCTAssertNil(user?["name"])
    }

    func testReplacingFoundryScopeRemovesStaleKeysAndPreservesUnownedValues() {
        let scope = Scope()
        scope.setTag(value: "native", key: "native-tag")
        scope.setContext(value: ["os": "macOS"], key: "device")
        let first = foundryScopePayload([
            "tags": ["region": "iad", "mode": "ranked"],
            "contexts": [
                "match": ["id": "match-9"],
                "round": ["number": 2],
            ],
            "user": ["id": "player-7"],
        ])
        let firstKeys = replaceFoundryScope(
            first,
            previousKeys: FoundryInstalledScopeKeys(),
            on: scope
        )
        let second = foundryScopePayload([
            "tags": ["region": "fra"],
            "contexts": ["match": ["id": "match-10"]],
            "user": ["id": "player-8"],
        ])

        let secondKeys = replaceFoundryScope(
            second,
            previousKeys: firstKeys,
            on: scope
        )

        let serialized = scope.serialize()
        let tags = serialized["tags"] as? [String: String]
        XCTAssertEqual(tags?["native-tag"], "native")
        XCTAssertEqual(tags?["region"], "fra")
        XCTAssertNil(tags?["mode"])
        let contexts = serialized["context"] as? [String: Any]
        XCTAssertEqual(
            (contexts?["device"] as? [String: Any])?["os"] as? String,
            "macOS"
        )
        XCTAssertEqual(
            (contexts?["match"] as? [String: Any])?["id"] as? String,
            "match-10"
        )
        XCTAssertNil(contexts?["round"])
        XCTAssertEqual(
            (serialized["user"] as? [String: Any])?["id"] as? String,
            "player-8"
        )
        XCTAssertEqual(secondKeys.tagKeys, ["region"])
        XCTAssertEqual(secondKeys.contextKeys, ["match"])
    }

    func testEmptyFoundryScopeRemovesPreviouslyInstalledValues() {
        let scope = Scope()
        let installedKeys = replaceFoundryScope(
            foundryScopePayload([
                "tags": ["region": "iad"],
                "contexts": ["match": ["id": "match-9"]],
                "user": ["id": "player-7"],
            ]),
            previousKeys: FoundryInstalledScopeKeys(),
            on: scope
        )

        let emptyKeys = replaceFoundryScope(
            foundryScopePayload([:]),
            previousKeys: installedKeys,
            on: scope
        )

        let serialized = scope.serialize()
        XCTAssertNil((serialized["tags"] as? [String: String])?["region"])
        XCTAssertNil((serialized["context"] as? [String: Any])?["match"])
        XCTAssertNil(serialized["user"])
        XCTAssertTrue(emptyKeys.tagKeys.isEmpty)
        XCTAssertTrue(emptyKeys.contextKeys.isEmpty)
    }

    func testEventLocalScopeOverlaysGlobalWithoutRemovingOrMutatingIt() {
        let globalScope = Scope()
        applyFoundryScope(
            foundryScopePayload([
                "tags": ["region": "iad", "mode": "ranked"],
                "contexts": [
                    "match": ["id": "global", "round": 1],
                    "device": ["class": "desktop"],
                ],
                "user": ["id": "global-player"],
            ]),
            to: globalScope
        )
        let captureScope = Scope(scope: globalScope)

        applyFoundryScope(
            foundryScopePayload([
                "tags": ["region": "fra"],
                "contexts": ["match": ["id": "local"]],
                "user": ["id": "local-player"],
            ]),
            to: captureScope
        )

        let captured = captureScope.serialize()
        XCTAssertEqual(
            (captured["tags"] as? [String: String])?["region"],
            "fra"
        )
        XCTAssertEqual(
            (captured["tags"] as? [String: String])?["mode"],
            "ranked"
        )
        XCTAssertEqual(
            ((captured["context"] as? [String: Any])?["match"] as? [String: Any])?["id"]
                as? String,
            "local"
        )
        XCTAssertNil(
            ((captured["context"] as? [String: Any])?["match"] as? [String: Any])?["round"]
        )
        XCTAssertEqual(
            ((captured["context"] as? [String: Any])?["device"] as? [String: Any])?["class"]
                as? String,
            "desktop"
        )
        XCTAssertEqual(
            (captured["user"] as? [String: Any])?["id"] as? String,
            "local-player"
        )

        let global = globalScope.serialize()
        XCTAssertEqual(
            (global["tags"] as? [String: String])?["region"],
            "iad"
        )
        XCTAssertEqual(
            ((global["context"] as? [String: Any])?["match"] as? [String: Any])?["id"]
                as? String,
            "global"
        )
        XCTAssertEqual(
            (global["user"] as? [String: Any])?["id"] as? String,
            "global-player"
        )
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

    func testMapsStructuredExceptionFramesToSentryStacktrace() {
        let payload = foundryExceptionPayload([
            "type_name": "InvalidState",
            "message": "bad state",
            "stack_trace": "legacy formatted stack",
            "attributes": ["entity": "player"],
            "frames": [
                [
                    "file": "res://Player.fs",
                    "function": "Player.attack",
                    "line": Int64(24),
                    "language": "fsharp",
                    "in_app": true,
                    "context_line": "let damage = 10",
                    "pre_context": ["let weapon = sword", "let target = goblin"],
                    "post_context": ["applyDamage target damage"],
                    "variables": ["damage": 10, "critical": false],
                ],
                [
                    "file": "res://Combat.fs",
                    "function": "Combat.resolve",
                    "line": NSNumber(value: 8),
                    "language": "fsharp",
                    "in_app": false,
                ],
                [
                    "file": "res://NSNumberLine.fs",
                    "line": NSNumber(value: 1),
                    "in_app": NSNumber(value: false),
                ],
                [
                    "file": "res://MalformedInApp.fs",
                    "in_app": NSNumber(value: 8.5),
                ],
                [
                    "file": "res://MissingInApp.fs",
                ],
            ],
        ])

        guard let payload else {
            XCTFail("Expected exception payload")
            return
        }
        let event = makeSentryEvent(
            message: "boom",
            level: 50,
            source: "combat",
            kind: "exception",
            timestampMsec: 1_612_325_106_123,
            engineTicksMsec: 4567,
            exception: payload
        )

        XCTAssertEqual(event.extra?["foundry.stack_trace"] as? String, "legacy formatted stack")
        guard let frames = event.exceptions?.first?.stacktrace?.frames else {
            XCTFail("Expected structured Sentry stacktrace")
            return
        }
        XCTAssertEqual(frames.count, 5)

        XCTAssertEqual(frames[0].fileName, "res://Player.fs")
        XCTAssertEqual(frames[0].function, "Player.attack")
        XCTAssertEqual(frames[0].lineNumber?.intValue, 24)
        XCTAssertEqual(frames[0].platform, "fsharp")
        XCTAssertEqual(frames[0].inApp?.boolValue, true)
        XCTAssertEqual(frames[0].contextLine, "let damage = 10")
        XCTAssertEqual(frames[0].preContext, ["let weapon = sword", "let target = goblin"])
        XCTAssertEqual(frames[0].postContext, ["applyDamage target damage"])
        XCTAssertEqual(frames[0].vars?["damage"] as? Int, 10)
        XCTAssertEqual(frames[0].vars?["critical"] as? Bool, false)

        XCTAssertEqual(frames[1].fileName, "res://Combat.fs")
        XCTAssertEqual(frames[1].function, "Combat.resolve")
        XCTAssertEqual(frames[1].lineNumber?.intValue, 8)
        XCTAssertEqual(frames[1].platform, "fsharp")
        XCTAssertEqual(frames[1].inApp?.boolValue, false)

        XCTAssertEqual(frames[2].fileName, "res://NSNumberLine.fs")
        XCTAssertEqual(frames[2].lineNumber?.intValue, 1)
        XCTAssertEqual(frames[2].inApp?.boolValue, false)

        XCTAssertEqual(frames[3].fileName, "res://MalformedInApp.fs")
        XCTAssertNil(frames[3].lineNumber)
        XCTAssertEqual(frames[3].inApp?.boolValue, true)

        XCTAssertEqual(frames[4].fileName, "res://MissingInApp.fs")
        XCTAssertEqual(frames[4].inApp?.boolValue, true)
    }

    func testSkipsMalformedFramesAndRetainsPartialFrames() {
        let payload = foundryExceptionPayload([
            "type_name": "InvalidState",
            "message": "bad state",
            "stack_trace": "legacy formatted stack",
            "frames": [
                "not a frame",
                [
                    "file": 42,
                    "function": false,
                    "line": -1,
                    "language": 99,
                    "in_app": "yes",
                    "context_line": "context without identity",
                    "pre_context": ["valid", 2, false],
                    "post_context": [true, 4],
                    "variables": ["not", "a dictionary"],
                ],
                [
                    "file": "",
                    "function": "",
                    "language": "",
                    "line": -1,
                    "context_line": "context with empty identity",
                    "pre_context": ["nearby"],
                    "variables": ["value": 1],
                ],
                ["context_line": "context only", "pre_context": ["nearby"]],
                ["variables": ["value": 1]],
                ["in_app": false],
                [
                    "file": "res://partial.fs",
                    "function": "",
                    "language": "",
                    "line": -1,
                    "context_line": "",
                    "pre_context": ["discarded"],
                    "post_context": ["also discarded"],
                ],
                ["line": 0],
                ["line": true],
                ["line": NSNumber(value: true)],
            ],
        ])

        guard let payload else {
            XCTFail("Expected exception payload")
            return
        }
        let event = makeSentryEvent(
            message: "boom",
            level: 50,
            source: "combat",
            kind: "exception",
            timestampMsec: 1_612_325_106_123,
            engineTicksMsec: 4567,
            exception: payload
        )

        XCTAssertEqual(event.extra?["foundry.stack_trace"] as? String, "legacy formatted stack")
        guard let frames = event.exceptions?.first?.stacktrace?.frames else {
            XCTFail("Expected partial frame stacktrace")
            return
        }
        XCTAssertEqual(frames.count, 1)
        XCTAssertEqual(frames[0].fileName, "res://partial.fs")
        XCTAssertNil(frames[0].function)
        XCTAssertNil(frames[0].lineNumber)
        XCTAssertNil(frames[0].platform)
        XCTAssertEqual(frames[0].inApp?.boolValue, true)
        XCTAssertNil(frames[0].contextLine)
        XCTAssertNil(frames[0].preContext)
        XCTAssertNil(frames[0].postContext)
    }

    func testSanitizesAndCopiesNestedFrameVariables() {
        let nested = NSMutableDictionary()
        nested["kept"] = "nested value"
        nested[NSNumber(value: 7)] = "discarded key"
        nested["unsupported"] = NSObject()
        nested["nan"] = Double.nan

        let list = NSMutableArray()
        list.add("list value")
        list.add(Int64(2))
        list.add(Double.infinity)
        list.add(NSObject())

        let cycle = NSMutableDictionary()
        cycle["kept"] = "cycle value"
        cycle["self"] = cycle

        let mutualCycle = NSMutableDictionary()
        let mutualChild = NSMutableDictionary()
        mutualCycle["child"] = mutualChild
        mutualChild["kept"] = "mutual value"
        mutualChild["parent"] = mutualCycle

        let repeatedArray = NSMutableArray(array: ["repeated value"])
        let repeatedDictionary = NSMutableDictionary(dictionary: ["kept": "repeated dictionary"])
        let variables = NSMutableDictionary()
        variables["nested"] = nested
        variables["list"] = list
        variables["cycle"] = cycle
        variables["mutual_cycle"] = mutualCycle
        variables["first_repeat"] = repeatedArray
        variables["second_repeat"] = repeatedArray
        variables["first_dictionary"] = repeatedDictionary
        variables["second_dictionary"] = repeatedDictionary
        variables["finite_number"] = NSNumber(value: 2.5)
        variables["positive_infinity"] = Float.infinity

        let event = eventWithFrameVariables(variables, file: "res://variables.fs")

        nested["kept"] = "mutated nested value"
        list[0] = "mutated list value"
        cycle["kept"] = "mutated cycle value"
        mutualChild["kept"] = "mutated mutual value"
        repeatedArray[0] = "mutated repeated value"
        repeatedDictionary["kept"] = "mutated repeated dictionary"

        guard let sanitized = event.exceptions?.first?.stacktrace?.frames.first?.vars else {
            XCTFail("Expected sanitized frame variables")
            return
        }
        let nestedCopy = sanitized["nested"] as? [String: Any]
        XCTAssertEqual(nestedCopy?["kept"] as? String, "nested value")
        XCTAssertEqual(nestedCopy?.count, 1)

        let listCopy = sanitized["list"] as? [Any]
        XCTAssertEqual(listCopy?.count, 2)
        XCTAssertEqual(listCopy?[0] as? String, "list value")
        XCTAssertEqual(listCopy?[1] as? Int64, 2)

        let cycleCopy = sanitized["cycle"] as? [String: Any]
        XCTAssertEqual(cycleCopy?["kept"] as? String, "cycle value")
        XCTAssertFalse(cycleCopy?.keys.contains("self") ?? true)

        let mutualCopy = sanitized["mutual_cycle"] as? [String: Any]
        let mutualChildCopy = mutualCopy?["child"] as? [String: Any]
        XCTAssertEqual(mutualChildCopy?["kept"] as? String, "mutual value")
        XCTAssertFalse(mutualChildCopy?.keys.contains("parent") ?? true)

        guard
            var firstRepeat = sanitized["first_repeat"] as? [Any],
            let secondRepeat = sanitized["second_repeat"] as? [Any],
            var firstDictionary = sanitized["first_dictionary"] as? [String: Any],
            let secondDictionary = sanitized["second_dictionary"] as? [String: Any]
        else {
            XCTFail("Expected both repeated acyclic containers")
            return
        }
        XCTAssertEqual(firstRepeat.first as? String, "repeated value")
        XCTAssertEqual(secondRepeat.first as? String, "repeated value")

        firstRepeat[0] = "mutated first output copy"

        XCTAssertEqual(secondRepeat.first as? String, "repeated value")
        XCTAssertEqual(
            (sanitized["first_repeat"] as? [Any])?.first as? String,
            "repeated value"
        )
        XCTAssertEqual(
            (sanitized["second_repeat"] as? [Any])?.first as? String,
            "repeated value"
        )

        XCTAssertEqual(firstDictionary["kept"] as? String, "repeated dictionary")
        XCTAssertEqual(secondDictionary["kept"] as? String, "repeated dictionary")
        firstDictionary["kept"] = "mutated first dictionary copy"
        XCTAssertEqual(secondDictionary["kept"] as? String, "repeated dictionary")
        XCTAssertEqual(
            (sanitized["first_dictionary"] as? [String: Any])?["kept"] as? String,
            "repeated dictionary"
        )
        XCTAssertEqual(
            (sanitized["second_dictionary"] as? [String: Any])?["kept"] as? String,
            "repeated dictionary"
        )
        XCTAssertEqual((sanitized["finite_number"] as? NSNumber)?.doubleValue, 2.5)
        XCTAssertNil(sanitized["positive_infinity"])
    }

    func testBoundsFrameVariableDepthAndExaminedItemCount() {
        var itemVariables: [String: Any] = [:]
        for index in 0..<257 {
            itemVariables["item\(index)"] = index
        }
        let itemEvent = eventWithFrameVariables(itemVariables, file: "res://items.fs")
        XCTAssertEqual(
            itemEvent.exceptions?.first?.stacktrace?.frames.first?.vars?.count,
            256
        )

        let depthVariables = NSMutableDictionary()
        var current = depthVariables
        for _ in 0..<9 {
            let child = NSMutableDictionary()
            current["child"] = child
            current = child
        }
        current["leaf"] = "discarded"

        let depthEvent = eventWithFrameVariables(depthVariables, file: "res://depth.fs")
        guard var sanitized = depthEvent.exceptions?.first?.stacktrace?.frames.first?.vars else {
            XCTFail("Expected depth-bounded variables")
            return
        }
        for _ in 0..<8 {
            guard let child = sanitized["child"] as? [String: Any] else {
                XCTFail("Expected nested container through depth 8")
                return
            }
            sanitized = child
        }
        XCTAssertNil(sanitized["child"])
    }

    func testStringOnlyExceptionHasNoStructuredStacktrace() {
        let payload = foundryExceptionPayload([
            "type_name": "InvalidState",
            "message": "bad state",
            "stack_trace": "at Player.attack()",
        ])

        guard let payload else {
            XCTFail("Expected exception payload")
            return
        }
        let event = makeSentryEvent(
            message: "boom",
            level: 50,
            source: "combat",
            kind: "exception",
            timestampMsec: 1_612_325_106_123,
            engineTicksMsec: 4567,
            exception: payload
        )

        XCTAssertEqual(event.extra?["foundry.stack_trace"] as? String, "at Player.attack()")
        XCTAssertNil(event.exceptions?.first?.stacktrace)
    }

    private func eventWithFrameVariables(_ variables: Any, file: String) -> Event {
        let payload = foundryExceptionPayload([
            "type_name": "InvalidState",
            "message": "bad state",
            "frames": [[
                "file": file,
                "variables": variables,
            ]],
        ])
        return makeSentryEvent(
            message: "boom",
            level: 50,
            source: "combat",
            kind: "exception",
            timestampMsec: 1_612_325_106_123,
            engineTicksMsec: 4567,
            exception: payload
        )
    }
}
