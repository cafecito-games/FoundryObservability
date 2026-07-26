import Foundation
@preconcurrency import Sentry

private let foundryEventAttachmentCategory = "event.attachment"
private let foundryViewHierarchyAttachmentCategory = "event.view_hierarchy"

protocol FoundryAttachmentScope: AnyObject {
    func addAttachment(_ attachment: Attachment)
}

extension Scope: FoundryAttachmentScope {}

struct FoundrySentryCapturePreparation {
    let event: Event
    private let contexts: [String: [String: Any]]
    private let localScope: FoundryScopePayload
    private let attachments: [Attachment]

    init(
        event: Event,
        contexts: [String: [String: Any]],
        localScope: FoundryScopePayload,
        attachments: [Attachment]
    ) {
        self.event = event
        self.contexts = contexts
        self.localScope = localScope
        self.attachments = attachments
    }

    func apply(
        to scope: Scope,
        addAttachment: (Scope, Attachment) -> Void = {
            scope, attachment in
            scope.addAttachment(attachment)
        }
    ) {
        applySentryContexts(contexts, to: scope)
        applyFoundryScope(localScope, to: scope)
        for attachment in attachments {
            addAttachment(scope, attachment)
        }
    }
}

func foundryAttachment(from payload: [String: Any]) -> Attachment? {
    guard
        !payload.isEmpty,
        let filename = payload["filename"] as? String,
        !filename.isEmpty,
        let category = payload["category"] as? String,
        let attachmentType = attachmentType(for: category)
    else {
        return nil
    }
    let contentType: String?
    if payload.keys.contains("content_type") {
        guard let candidate = payload["content_type"] as? String else {
            return nil
        }
        contentType = candidate.isEmpty ? nil : candidate
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

func prepareFoundrySentryCapture(
    values: [String: Any],
    globalAttributes: [String: Any],
    attachments: [Attachment]
) -> FoundrySentryCapturePreparation {
    let event = makeSentryEvent(
        message: captureStringValue(values["message"]),
        level: captureIntValue(values["level"]),
        source: captureStringValue(values["source"]),
        kind: captureStringValue(values["kind"]),
        timestampMsec: Int64(captureIntValue(values["timestamp_msec"])),
        engineTicksMsec: Int64(captureIntValue(values["engine_ticks_msec"])),
        globalAttributes: globalAttributes,
        eventAttributes: captureDictionaryValue(values["attributes"]),
        exception: foundryExceptionPayload(values["exception"])
    )
    return FoundrySentryCapturePreparation(
        event: event,
        contexts: foundrySentryContexts(values["contexts"]),
        localScope: foundryScopePayload(values["scope"]),
        attachments: attachments
    )
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

private func captureStringValue(_ value: Any?) -> String {
    value as? String ?? ""
}

private func captureIntValue(_ value: Any?) -> Int {
    if let value = value as? Int {
        return value
    }
    if let value = value as? Int64 {
        return Int(value)
    }
    if let value = value as? Double {
        return Int(value)
    }
    return 0
}

private func captureDictionaryValue(_ value: Any?) -> [String: Any] {
    value as? [String: Any] ?? [:]
}
