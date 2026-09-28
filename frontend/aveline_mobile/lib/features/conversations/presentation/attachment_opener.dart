import 'dart:io';
import 'dart:typed_data';

import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';

/// Hands a document's bytes to whatever the platform opens that type with.
///
/// Injectable so a widget test never writes a file or leaves the app.
typedef AttachmentOpener = Future<void> Function(Uint8List bytes, String fileName);

/// Writes the bytes to the app's temporary directory and opens them with the OS viewer.
///
/// A document cannot be previewed in-process the way an image can, so the file has to exist on
/// disk for the platform to hand it to a viewer. The temporary directory is the right home: the
/// bytes are a cache of what the API already stores, and the OS reclaims the space.
Future<void> openWithPlatformViewer(Uint8List bytes, String fileName) async {
  final directory = await getTemporaryDirectory();
  final file = File('${directory.path}/$fileName');
  await file.writeAsBytes(bytes, flush: true);
  await OpenFilex.open(file.path);
}
