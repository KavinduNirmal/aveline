import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

/// Where a picked file came from.
enum AttachmentSource { gallery, camera }

/// Offers the gallery or the camera, so the source is a deliberate choice rather than a
/// hidden long-press. Returns `null` when the sheet is dismissed.
///
/// Shared by the client thread's composer and the Salon's, so both ask the same question
/// and a test names the composer it means through [keyPrefix].
Future<AttachmentSource?> chooseAttachmentSource(
  BuildContext context, {
  String keyPrefix = 'thread',
}) {
  return showModalBottomSheet<AttachmentSource>(
    context: context,
    builder: (context) => SafeArea(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          ListTile(
            key: ValueKey('${keyPrefix}_attach_gallery'),
            leading: const Icon(Icons.photo_library_outlined),
            title: const Text('Photo library'),
            onTap: () => Navigator.of(context).pop(AttachmentSource.gallery),
          ),
          ListTile(
            key: ValueKey('${keyPrefix}_attach_camera'),
            leading: const Icon(Icons.photo_camera_outlined),
            title: const Text('Camera'),
            onTap: () => Navigator.of(context).pop(AttachmentSource.camera),
          ),
        ],
      ),
    ),
  );
}

/// One file the platform picker handed back, ready to upload.
class PickedAttachment {
  const PickedAttachment({
    required this.bytes,
    required this.contentType,
    required this.fileName,
  });

  final Uint8List bytes;
  final String contentType;
  final String fileName;
}

/// Opens the platform picker. Injectable so a widget test never touches the plugin.
typedef AttachmentPicker =
    Future<List<PickedAttachment>> Function(AttachmentSource source);

/// The shipped picker: the photo library or the camera, re-encoded under the API's cap.
///
/// Images are resized and re-compressed by the picker (`maxWidth`/`imageQuality`) so a phone
/// photo lands under the 5 MB limit before it is uploaded; a PDF is passed through as-is and
/// rejected by the server if it is over the cap.
Future<List<PickedAttachment>> pickWithImagePicker(AttachmentSource source) async {
  final picker = ImagePicker();
  final files = <XFile>[];

  if (source == AttachmentSource.camera) {
    final shot = await picker.pickImage(
      source: ImageSource.camera,
      maxWidth: 1600,
      imageQuality: 82,
    );
    if (shot != null) {
      files.add(shot);
    }
  } else {
    files.addAll(
      await picker.pickMultiImage(maxWidth: 1600, imageQuality: 82),
    );
  }

  final attachments = <PickedAttachment>[];
  for (final file in files) {
    final declared = file.mimeType;
    attachments.add(
      PickedAttachment(
        bytes: await file.readAsBytes(),
        // The picker reports the type on most platforms; the extension covers the rest, and the
        // server resolves the same way when the header is generic.
        contentType: declared == null || declared.isEmpty
            ? contentTypeForFileName(file.name)
            : declared,
        fileName: file.name,
      ),
    );
  }
  return attachments;
}

/// The media type a file name implies, matching the API's allow-list.
String contentTypeForFileName(String fileName) {
  final dot = fileName.lastIndexOf('.');
  final extension = dot < 0 ? '' : fileName.substring(dot).toLowerCase();
  return switch (extension) {
    '.pdf' => 'application/pdf',
    '.png' => 'image/png',
    '.webp' => 'image/webp',
    '.gif' => 'image/gif',
    '.avif' => 'image/avif',
    '.bmp' => 'image/bmp',
    '.tif' || '.tiff' => 'image/tiff',
    '.heic' => 'image/heic',
    '.heif' => 'image/heif',
    _ => 'image/jpeg',
  };
}
