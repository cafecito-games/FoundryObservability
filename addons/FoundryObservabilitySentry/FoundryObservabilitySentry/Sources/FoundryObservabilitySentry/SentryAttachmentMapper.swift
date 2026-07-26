import Foundation
@preconcurrency import Sentry

private let foundryEventAttachmentCategory = "event.attachment"
private let foundryViewHierarchyAttachmentCategory = "event.view_hierarchy"

protocol FoundryAttachmentScope: AnyObject {
    func addAttachment(_ attachment: Attachment)
}

extension Scope: FoundryAttachmentScope {}

enum FoundrySentryCaptureRoute: CaseIterable {
    case event
    case message
    case exception
}

func foundryAttachment(from payload: [String: Any]) -> Attachment? {
    guard
        !payload.isEmpty,
        let filename = payload["filename"] as? String,
        isSafeAttachmentFilename(filename),
        let category = payload["category"] as? String,
        let attachmentType = attachmentType(for: category)
    else {
        return nil
    }
    let contentType: String?
    if payload.keys.contains("content_type") {
        guard
            let candidate = payload["content_type"] as? String,
            !candidate.isEmpty
        else {
            return nil
        }
        contentType = candidate
    } else {
        contentType = nil
    }

    let hasPath = payload.keys.contains("path")
    let hasBytes = payload.keys.contains("bytes")
    guard hasPath != hasBytes else {
        return nil
    }

    if hasPath {
        guard
            let path = payload["path"] as? String,
            path.hasPrefix("/"),
            !path.isEmpty
        else {
            return nil
        }
        return Attachment(
            path: path,
            filename: filename,
            contentType: contentType,
            attachmentType: attachmentType
        )
    }

    guard let data = payload["bytes"] as? Data else {
        return nil
    }
    let bytes = attachmentBytes(data)
    return Attachment(
        data: bytes,
        filename: filename,
        contentType: contentType,
        attachmentType: attachmentType
    )
}

func foundryAttachments(from payloads: [[String: Any]]) -> [Attachment]? {
    var attachments: [Attachment] = []
    attachments.reserveCapacity(payloads.count)
    for payload in payloads {
        guard let attachment = foundryAttachment(from: payload) else {
            return nil
        }
        attachments.append(attachment)
    }
    return attachments
}

func applyFoundryAttachments(
    _ attachments: [Attachment],
    to scope: FoundryAttachmentScope
) {
    for attachment in attachments {
        scope.addAttachment(attachment)
    }
}

func withFoundryCaptureAttachments(
    _ attachments: [Attachment],
    scope: FoundryAttachmentScope,
    route: FoundrySentryCaptureRoute,
    capture: (FoundrySentryCaptureRoute) -> Void
) {
    applyFoundryAttachments(attachments, to: scope)
    capture(route)
}

private func attachmentType(for category: String) -> SentryAttachmentType? {
    switch category {
    case foundryEventAttachmentCategory:
        return .eventAttachment
    case foundryViewHierarchyAttachmentCategory:
        return .viewHierarchy
    default:
        return nil
    }
}

private func attachmentBytes(_ data: Data) -> Data {
    data.withUnsafeBytes { Data($0) }
}

private func isSafeAttachmentFilename(_ filename: String) -> Bool {
    !filename.isEmpty
        && filename != "."
        && filename != ".."
        && !filename.contains("/")
        && !filename.contains("\\")
        && !filename.contains("\0")
        && !filename.allSatisfy(\.isWhitespace)
}
