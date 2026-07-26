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

    func testRejectsMalformedAttachmentPayloads() {
        let malformed: [[String: Any]] = [
            [:],
            ["filename": "", "category": "event.attachment", "path": "/tmp/a"],
            ["filename": " ", "category": "event.attachment", "path": "/tmp/a"],
            ["filename": ".", "category": "event.attachment", "path": "/tmp/a"],
            ["filename": "..", "category": "event.attachment", "path": "/tmp/a"],
            ["filename": "dir/a", "category": "event.attachment", "path": "/tmp/a"],
            ["filename": "dir\\a", "category": "event.attachment", "path": "/tmp/a"],
            ["filename": "a", "category": "", "path": "/tmp/a"],
            ["filename": "a", "category": "other", "path": "/tmp/a"],
            ["filename": "a", "category": "event.attachment", "path": "relative/a"],
            ["filename": "a", "category": "event.attachment", "path": ""],
            ["filename": "a", "category": "event.attachment", "path": "/tmp/a", "bytes": Data([1])],
            ["filename": "a", "category": "event.attachment", "bytes": [1, 2]],
            ["filename": "a", "category": "event.attachment"],
            ["filename": "a", "category": "event.attachment", "path": "/tmp/a", "content_type": ""],
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

    func testCaptureLocalAttachmentsAreAppliedOnlyToCaptureScope() throws {
        let global = AttachmentScopeSpy()
        let capture = AttachmentScopeSpy()
        let attachments = [
            try XCTUnwrap(foundryAttachment(from: [
                "filename": "local.bin",
                "category": "event.attachment",
                "bytes": Data([4]),
            ])),
        ]

        applyFoundryAttachments(attachments, to: capture)

        XCTAssertTrue(global.filenames.isEmpty)
        XCTAssertEqual(capture.filenames, ["local.bin"])
    }

    func testEventMessageAndExceptionCaptureRoutesUseLocalAttachments() throws {
        let attachment = try XCTUnwrap(foundryAttachment(from: [
            "filename": "local.bin",
            "category": "event.attachment",
            "bytes": Data([4]),
        ]))

        for route in FoundrySentryCaptureRoute.allCases {
            let scope = AttachmentScopeSpy()
            var invokedRoute: FoundrySentryCaptureRoute?
            withFoundryCaptureAttachments(
                [attachment],
                scope: scope,
                route: route
            ) { candidateRoute in
                invokedRoute = candidateRoute
            }
            XCTAssertEqual(invokedRoute, route)
            XCTAssertEqual(scope.filenames, ["local.bin"])
        }
    }
}

private final class AttachmentScopeSpy: FoundryAttachmentScope {
    var filenames: [String] = []

    func addAttachment(_ attachment: Attachment) {
        filenames.append(attachment.filename)
    }
}
