import 'package:flutter/material.dart';

import '../../domain/outfit_composition.dart';
import '../../domain/outfit_payloads.dart';
import '../lookbooks_controller.dart';

/// Modal dialog to edit lookbook title, occasion, and styling commentary.
class EditLookbookDialog extends StatefulWidget {
  const EditLookbookDialog({
    super.key,
    required this.lookbook,
    required this.onSave,
  });

  final OutfitComposition lookbook;
  final Future<void> Function(UpdateLookbookPayload payload) onSave;

  @override
  State<EditLookbookDialog> createState() => _EditLookbookDialogState();
}

class _EditLookbookDialogState extends State<EditLookbookDialog> {
  late final TextEditingController _nameController;
  late final TextEditingController _styleNotesController;
  late String _selectedOccasion;
  bool _isSaving = false;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    _nameController = TextEditingController(text: widget.lookbook.name);
    _styleNotesController = TextEditingController(text: widget.lookbook.styleNotes);
    _selectedOccasion = widget.lookbook.occasion;
  }

  @override
  void dispose() {
    _nameController.dispose();
    _styleNotesController.dispose();
    super.dispose();
  }

  Future<void> _handleSave() async {
    final name = _nameController.text.trim();
    if (name.isEmpty) {
      setState(() => _errorMessage = 'Please enter a name for the lookbook.');
      return;
    }

    setState(() {
      _isSaving = true;
      _errorMessage = null;
    });

    try {
      final payload = UpdateLookbookPayload(
        name: name,
        occasion: _selectedOccasion,
        styleNotes: _styleNotesController.text.trim(),
      );
      await widget.onSave(payload);
      if (mounted) Navigator.of(context).pop();
    } catch (e) {
      if (mounted) {
        setState(() {
          _isSaving = false;
          _errorMessage = 'Failed to save lookbook: $e';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final availableOccasions = lookbookOccasions.where((o) => o != 'All').toList();

    return Dialog(
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
      backgroundColor: scheme.surface,
      insetPadding: const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 480),
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Expanded(
                    child: Text(
                      'Edit Lookbook Details',
                      style: theme.textTheme.titleMedium?.copyWith(
                        fontWeight: FontWeight.w600,
                        fontFamily: 'PlayfairDisplay',
                      ),
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                  IconButton(
                    icon: const Icon(Icons.close, size: 20),
                    onPressed: () => Navigator.of(context).pop(),
                  ),
                ],
              ),
              const SizedBox(height: 16),

              if (_errorMessage != null) ...[
                Container(
                  padding: const EdgeInsets.all(10),
                  margin: const EdgeInsets.only(bottom: 12),
                  decoration: BoxDecoration(
                    color: scheme.errorContainer,
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: Text(
                    _errorMessage!,
                    style: TextStyle(color: scheme.onErrorContainer, fontSize: 13),
                  ),
                ),
              ],

              Text(
                'Lookbook Title *',
                style: theme.textTheme.labelMedium?.copyWith(
                  color: scheme.onSurfaceVariant,
                  fontWeight: FontWeight.w500,
                ),
              ),
              const SizedBox(height: 6),
              TextField(
                key: const Key('edit_lookbook_name_input'),
                controller: _nameController,
                decoration: InputDecoration(
                  hintText: 'e.g. Royal Sangeet Emerald Look',
                  contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                  filled: true,
                  fillColor: scheme.surfaceContainerLowest,
                ),
              ),
              const SizedBox(height: 14),

              Text(
                'Ceremonial Occasion',
                style: theme.textTheme.labelMedium?.copyWith(
                  color: scheme.onSurfaceVariant,
                  fontWeight: FontWeight.w500,
                ),
              ),
              const SizedBox(height: 6),
              DropdownButtonFormField<String>(
                key: const Key('edit_lookbook_occasion_dropdown'),
                isExpanded: true,
                initialValue: availableOccasions.contains(_selectedOccasion)
                    ? _selectedOccasion
                    : availableOccasions.first,
                items: availableOccasions.map((occ) {
                  return DropdownMenuItem<String>(
                    value: occ,
                    child: Text(occ, style: const TextStyle(fontSize: 13)),
                  );
                }).toList(),
                onChanged: (val) {
                  if (val != null) setState(() => _selectedOccasion = val);
                },
                decoration: InputDecoration(
                  contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                  filled: true,
                  fillColor: scheme.surfaceContainerLowest,
                ),
              ),
              const SizedBox(height: 14),

              Text(
                'Styling Recommendations & Notes',
                style: theme.textTheme.labelMedium?.copyWith(
                  color: scheme.onSurfaceVariant,
                  fontWeight: FontWeight.w500,
                ),
              ),
              const SizedBox(height: 6),
              TextField(
                key: const Key('edit_lookbook_notes_input'),
                controller: _styleNotesController,
                maxLines: 3,
                decoration: InputDecoration(
                  hintText: 'Draping advice, jewelry recommendations...',
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                  filled: true,
                  fillColor: scheme.surfaceContainerLowest,
                ),
              ),
              const SizedBox(height: 20),

              Row(
                mainAxisAlignment: MainAxisAlignment.end,
                children: [
                  TextButton(
                    onPressed: _isSaving ? null : () => Navigator.of(context).pop(),
                    child: const Text('Cancel'),
                  ),
                  const SizedBox(width: 8),
                  Flexible(
                    child: ElevatedButton(
                      key: const Key('edit_lookbook_save_button'),
                      onPressed: _isSaving ? null : _handleSave,
                      style: ElevatedButton.styleFrom(
                        backgroundColor: scheme.primary,
                        foregroundColor: scheme.onPrimary,
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
                      ),
                      child: _isSaving
                          ? const SizedBox(
                              width: 18,
                              height: 18,
                              child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                            )
                          : const Text('Save Changes', overflow: TextOverflow.ellipsis),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
