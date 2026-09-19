/// One file uploaded for a thread message, as the upload route returned it.
///
/// The bytes are never fetched from [url] directly: the URL is authenticated, so a client reads
/// them through the shared [Dio] (see `ThreadRepository.fetchAttachmentBytes`). [url] is what a
/// CDN-backed store would hand back later, which is why it travels on the row.
class ThreadAttachment {
  const ThreadAttachment({
    required this.id,
    required this.url,
    required this.contentType,
    required this.fileName,
    required this.sizeBytes,
    this.width,
    this.height,
  });

  final String id;
  final String url;
  final String contentType;
  final String fileName;
  final int sizeBytes;
  final int? width;
  final int? height;

  /// Whether this attachment renders as a thumbnail rather than a document chip.
  bool get isImage => contentType.startsWith('image/');

  factory ThreadAttachment.fromJson(Map<String, dynamic> json) => ThreadAttachment(
    id: json['attachmentId']?.toString() ?? '',
    url: json['url']?.toString() ?? '',
    contentType: json['contentType']?.toString() ?? 'application/octet-stream',
    fileName: json['fileName']?.toString() ?? 'attachment',
    sizeBytes: (json['sizeBytes'] as num?)?.toInt() ?? 0,
    width: (json['width'] as num?)?.toInt(),
    height: (json['height'] as num?)?.toInt(),
  );
}
