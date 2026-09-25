import 'dart:io';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

import '../../domain/vision_analysis.dart';

/// Camera / gallery photograph capture and AI vision analysis status section.
class ImageCaptureSection extends StatelessWidget {
  const ImageCaptureSection({
    super.key,
    this.localImageFile,
    this.networkImageUrl,
    this.isUploading = false,
    this.isAnalyzing = false,
    this.analysisResult,
    required this.onPickImage,
    required this.onClearImage,
  });

  final File? localImageFile;
  final String? networkImageUrl;
  final bool isUploading;
  final bool isAnalyzing;
  final VisionAnalysis? analysisResult;
  final void Function(ImageSource source) onPickImage;
  final VoidCallback onClearImage;

  bool get hasImage => localImageFile != null || (networkImageUrl != null && networkImageUrl!.trim().isNotEmpty);

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isBusy = isUploading || isAnalyzing;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        ClipRRect(
          borderRadius: BorderRadius.circular(16),
          child: Container(
            width: double.infinity,
            height: 220,
            decoration: BoxDecoration(
              color: scheme.surfaceContainerLow,
              border: Border.all(color: scheme.outlineVariant),
              borderRadius: BorderRadius.circular(16),
            ),
            child: Stack(
              fit: StackFit.expand,
              children: [
                if (localImageFile != null)
                  Image.file(
                    localImageFile!,
                    fit: BoxFit.cover,
                  )
                else if (networkImageUrl != null && networkImageUrl!.trim().isNotEmpty)
                  Image.network(
                    networkImageUrl!,
                    fit: BoxFit.cover,
                    errorBuilder: (context, error, stackTrace) => _buildPlaceholder(context),
                  )
                else
                  _buildPlaceholder(context),

                // Shimmer / Analyzing Overlay
                if (isBusy)
                  Container(
                    color: Colors.black54,
                    child: Center(
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          const SizedBox(
                            width: 28,
                            height: 28,
                            child: CircularProgressIndicator(
                              strokeWidth: 2.5,
                              valueColor: AlwaysStoppedAnimation<Color>(Colors.white),
                            ),
                          ),
                          const SizedBox(height: 12),
                          Text(
                            isUploading
                                ? 'Uploading photograph...'
                                : 'Analyzing with Elle Vision AI...',
                            style: theme.textTheme.labelMedium?.copyWith(
                              color: Colors.white,
                              fontWeight: FontWeight.w500,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),

                // Clear button when image exists and not busy
                if (hasImage && !isBusy)
                  Positioned(
                    top: 10,
                    right: 10,
                    child: Material(
                      color: Colors.black54,
                      shape: const CircleBorder(),
                      child: IconButton(
                        icon: const Icon(Icons.close, color: Colors.white, size: 18),
                        onPressed: onClearImage,
                        tooltip: 'Remove photo',
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 12),

        // Action Buttons: Camera & Gallery
        Row(
          children: [
            Expanded(
              child: OutlinedButton.icon(
                key: const Key('catalog_pick_camera'),
                onPressed: isBusy ? null : () => onPickImage(ImageSource.camera),
                icon: const Icon(Icons.camera_alt_outlined, size: 18),
                label: const Text('Take photo'),
                style: OutlinedButton.styleFrom(
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                  padding: const EdgeInsets.symmetric(vertical: 12),
                ),
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: OutlinedButton.icon(
                key: const Key('catalog_pick_gallery'),
                onPressed: isBusy ? null : () => onPickImage(ImageSource.gallery),
                icon: const Icon(Icons.photo_library_outlined, size: 18),
                label: const Text('From gallery'),
                style: OutlinedButton.styleFrom(
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                  padding: const EdgeInsets.symmetric(vertical: 12),
                ),
              ),
            ),
          ],
        ),

        // AI Results banner if analysis completed
        if (analysisResult != null) ...[
          const SizedBox(height: 14),
          Container(
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: scheme.primary.withValues(alpha: 0.08),
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: scheme.primary.withValues(alpha: 0.2)),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Icon(Icons.auto_awesome, size: 16, color: scheme.primary),
                    const SizedBox(width: 6),
                    Text(
                      analysisResult!.confidenceScore != null
                          ? '${(analysisResult!.confidenceScore! * 100).toInt()}% Vision AI'
                          : 'Vision AI Attribute Extraction',
                      style: theme.textTheme.labelMedium?.copyWith(
                        color: scheme.primary,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ],
                ),
                if (analysisResult!.visualAttributes.isNotEmpty) ...[
                  const SizedBox(height: 8),
                  Wrap(
                    spacing: 6,
                    runSpacing: 6,
                    children: [
                      for (final tag in analysisResult!.visualAttributes)
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                          decoration: BoxDecoration(
                            color: scheme.surface,
                            borderRadius: BorderRadius.circular(6),
                            border: Border.all(color: scheme.outlineVariant),
                          ),
                          child: Text(
                            tag,
                            style: theme.textTheme.labelSmall?.copyWith(
                              color: scheme.onSurface,
                            ),
                          ),
                        ),
                    ],
                  ),
                ],
              ],
            ),
          ),
        ],
      ],
    );
  }

  Widget _buildPlaceholder(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.add_photo_alternate_outlined, size: 44, color: scheme.onSurfaceVariant.withValues(alpha: 0.6)),
          const SizedBox(height: 8),
          Text(
            'Photograph the couture piece',
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
              fontWeight: FontWeight.w500,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            'Elle Vision AI will automatically detect fabric, color & category',
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.onSurfaceVariant.withValues(alpha: 0.8),
            ),
            textAlign: TextAlign.center,
          ),
        ],
      ),
    );
  }
}
