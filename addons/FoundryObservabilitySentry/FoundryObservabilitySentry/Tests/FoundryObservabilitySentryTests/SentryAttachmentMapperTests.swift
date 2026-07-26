import Foundation
import Sentry
import XCTest
@testable import FoundryObservabilitySentry

final class SentryAttachmentMapperTests: XCTestCase {
    func testMapsFileAttachmentAndPreservesMetadata() throws {
        let attachment = try XCTUnwrap(foundryAttachment(from: [
            "filename": "game.log",
            "content_type": "text/plain",
            "category": "event.attachment",
            "path": "/tmp/game.log",
        ]))

        XCTAssertEqual(attachment.path, "/tmp/game.log")
        XCTAssertNil(attachment.data)
        XCTAssertEqual(attachment.filename, "game.log")
        XCTAssertEqual(attachment.contentType, "text/plain")
        XCTAssertEqual(attachment.attachmentType, .eventAttachment)
    }

    func testMapsByteAttachmentAndCopiesData() throws {
        let source = NSMutableData(data: Data([1, 2, 3]))
        let attachment = try XCTUnwrap(foundryAttachment(from: [
            "filename": "view.json",
            "content_type": "application/json",
            "category": "event.view_hierarchy",
            "bytes": source as Data,
        ]))

        source.replaceBytes(in: NSRange(location: 0, length: 1), withBytes: [9])

        XCTAssertNil(attachment.path)
        XCTAssertEqual(attachment.data, Data([1, 2, 3]))
        XCTAssertEqual(attachment.filename, "view.json")
        XCTAssertEqual(attachment.contentType, "application/json")
        XCTAssertEqual(attachment.attachmentType, .viewHierarchy)
    }

    func testEmptyContentTypeNormalizesToNil() throws {
        let attachment = try XCTUnwrap(foundryAttachment(from: [
            "filename": "empty-type.bin",
            "content_type": "",
            "category": "event.attachment",
            "bytes": Data([1]),
        ]))

        XCTAssertNil(attachment.contentType)
    }

    func testPreservesNonemptyFilenameWithoutAdditionalPolicy() throws {
        for filename in [".", "..", "dir/file.log", "dir\\file.log", " "] {
            let attachment = try XCTUnwrap(foundryAttachment(from: [
                "filename": filename,
                "category": "event.attachment",
                "bytes": Data([1]),
            ]))

            XCTAssertEqual(attachment.filename, filename)
        }
    }

    func testRejectsMalformedAttachmentPayloads() {
        let malformed: [[String: Any]] = [
            [:],
            ["filename": "", "category": "event.attachment", "path": "/tmp/a"],
            ["filename": "a", "category": "", "path": "/tmp/a"],
            ["filename": "a", "category": "other", "path": "/tmp/a"],
            ["filename": "a", "category": "event.attachment", "path": "relative/a"],
            ["filename": "a", "category": "event.attachment", "path": ""],
            ["filename": "a", "category": "event.attachment", "path": "/tmp/a", "bytes": Data([1])],
            ["filename": "a", "category": "event.attachment", "bytes": [1, 2]],
            ["filename": "a", "category": "event.attachment"],
            ["filename": "a", "category": "event.attachment", "path": "/tmp/a", "content_type": 12],
            ["filename": 12, "category": "event.attachment", "path": "/tmp/a"],
            ["filename": "a", "category": 12, "path": "/tmp/a"],
        ]

        for payload in malformed {
            XCTAssertNil(foundryAttachment(from: payload), "Accepted \(payload)")
        }
    }

    func testParsesCompleteArrayAtomically() {
        XCTAssertNil(foundryAttachments(from: [
            [
                "filename": "good.bin",
                "category": "event.attachment",
                "bytes": Data([1]),
            ],
            [
                "filename": "bad.bin",
                "category": "event.attachment",
                "path": "relative",
            ],
        ]))

        XCTAssertEqual(foundryAttachments(from: [])?.count, 0)
    }

    func testStrictBridgeCandidateRejectsDroppedOrNondictionaryElements() {
        let valid = FoundryFoundationValue([
            "filename": "good.bin",
            "category": "event.attachment",
            "bytes": Data([1]),
        ])

        XCTAssertNil(strictFoundryAttachments(from: [valid, nil]))
        XCTAssertNil(strictFoundryAttachments(from: [
            valid,
            FoundryFoundationValue("unsupported"),
        ]))
        XCTAssertEqual(
            strictFoundryAttachments(from: [valid])?.map(\.filename),
            ["good.bin"]
        )
    }

    func testRealCapturePreparationHandlesMessageEventAndExceptionLocally() throws {
        let attachment = try XCTUnwrap(foundryAttachment(from: [
            "filename": "local.bin",
            "category": "event.attachment",
            "bytes": Data([4]),
        ]))
        let payloads: [[String: Any]] = [
            [
                "kind": "message",
                "level": 30,
                "message": "hello",
                "source": "game",
                "timestamp_msec": 1_000,
                "engine_ticks_msec": 25,
                "attributes": [:],
            ],
            [
                "kind": "event",
                "level": 40,
                "message": "round ended",
                "source": "match",
                "timestamp_msec": 2_000,
                "engine_ticks_msec": 50,
                "attributes": ["winner": "blue"],
            ],
            [
                "kind": "exception",
                "level": 50,
                "message": "bad state",
                "source": "game",
                "timestamp_msec": 3_000,
                "engine_ticks_msec": 75,
                "attributes": [:],
                "exception": [
                    "type_name": "InvalidState",
                    "message": "bad state",
                    "stack_trace": "frame",
                    "attributes": [:],
                ],
            ],
        ]

        for payload in payloads {
            var values = payload
            values["contexts"] = ["foundry_runtime": ["scene": "Arena"]]
            values["scope"] = ["tags": ["round": "final"]]
            let preparation = prepareFoundrySentryCapture(
                values: values,
                globalAttributes: ["build": 42],
                attachments: [attachment]
            )
            let globalScope = Scope()
            globalScope.setTag(value: "kept", key: "global")
            let captureScope = Scope(scope: globalScope)
            var capturedFilenames: [String] = []

            preparation.apply(to: captureScope) { _, candidate in
                capturedFilenames.append(candidate.filename)
            }

            XCTAssertEqual(preparation.event.message?.formatted, payload["message"] as? String)
            XCTAssertEqual(
                preparation.event.extra?["foundry.kind"] as? String,
                payload["kind"] as? String
            )
            XCTAssertEqual(capturedFilenames, ["local.bin"])
            let captured = captureScope.serialize()
            XCTAssertEqual(
                (captured["tags"] as? [String: String])?["round"],
                "final"
            )
            XCTAssertEqual(
                ((captured["context"] as? [String: Any])?["foundry_runtime"]
                    as? [String: Any])?["scene"] as? String,
                "Arena"
            )
            let global = globalScope.serialize()
            XCTAssertEqual(
                (global["tags"] as? [String: String])?["global"],
                "kept"
            )
            XCTAssertNil((global["tags"] as? [String: String])?["round"])
            XCTAssertNil(
                (global["context"] as? [String: Any])?["foundry_runtime"]
            )
        }

        let exceptionPreparation = prepareFoundrySentryCapture(
            values: payloads[2],
            globalAttributes: [:],
            attachments: [attachment]
        )
        XCTAssertEqual(
            exceptionPreparation.event.exceptions?.first?.type,
            "InvalidState"
        )
        XCTAssertEqual(
            exceptionPreparation.event.exceptions?.first?.value,
            "bad state"
        )
        XCTAssertEqual(
            exceptionPreparation.event.extra?["winner"] as? String,
            nil
        )
        let eventPreparation = prepareFoundrySentryCapture(
            values: payloads[1],
            globalAttributes: [:],
            attachments: [attachment]
        )
        XCTAssertEqual(eventPreparation.event.extra?["winner"] as? String, "blue")
    }

    func testCapturePreparationDoesNotApplyMalformedAttachmentCandidates() {
        let globalScope = Scope()
        globalScope.setTag(value: "kept", key: "global")

        XCTAssertNil(foundryAttachments(from: [[
            "filename": "bad.bin",
            "category": "event.attachment",
            "path": "relative",
        ]]))
        XCTAssertEqual(
            (globalScope.serialize()["tags"] as? [String: String])?["global"],
            "kept"
        )
    }
}
