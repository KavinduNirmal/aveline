import 'package:flutter/material.dart';

/// Collects just enough to start a walk-in client's profile.
///
/// Returns the name the associate typed, or `null` when the sheet is dismissed.
/// The **caller** creates the client, because a client invented here would have
/// an id nothing can resolve; the sheet only collects what the create call needs.
Future<String?> showQuickAddClientSheet(BuildContext context) {
  return showModalBottomSheet<String>(
    context: context,
    backgroundColor: Colors.transparent,
    isScrollControlled: true,
    builder: (context) => const _QuickAddClientSheet(),
  );
}

class _QuickAddClientSheet extends StatefulWidget {
  const _QuickAddClientSheet();

  @override
  State<_QuickAddClientSheet> createState() => _QuickAddClientSheetState();
}

class _QuickAddClientSheetState extends State<_QuickAddClientSheet> {
  final TextEditingController _name = TextEditingController();

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  bool get _canAdd => _name.text.trim().isNotEmpty;

  void _add() {
    final name = _name.text.trim();
    if (name.isEmpty) {
      return;
    }

    Navigator.of(context).pop(name);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Padding(
      // Lifts the sheet clear of the keyboard.
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SafeArea(
        top: false,
        child: Container(
          margin: const EdgeInsets.all(12),
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 20),
          decoration: BoxDecoration(
            color: scheme.surfaceContainerLowest,
            borderRadius: BorderRadius.circular(24),
            boxShadow: [
              BoxShadow(
                color: const Color(0xFF8B2E42).withValues(alpha: 0.12),
                blurRadius: 28,
                offset: const Offset(0, 10),
              ),
            ],
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Center(
                child: Container(
                  width: 40,
                  height: 4,
                  decoration: BoxDecoration(
                    color: scheme.outlineVariant,
                    borderRadius: BorderRadius.circular(2),
                  ),
                ),
              ),
              const SizedBox(height: 18),
              Text('Add a walk-in', style: theme.textTheme.headlineSmall),
              const SizedBox(height: 6),
              Text(
                'A name is enough to start their profile. Everything else can be filled in later.',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(height: 18),
              TextField(
                controller: _name,
                autofocus: true,
                textCapitalization: TextCapitalization.words,
                textInputAction: TextInputAction.done,
                onChanged: (_) => setState(() {}),
                onSubmitted: (_) => _add(),
                decoration: const InputDecoration(
                  labelText: 'Full name',
                  hintText: 'Maria Silva',
                ),
              ),
              const SizedBox(height: 20),
              SizedBox(
                width: double.infinity,
                child: FilledButton(
                  key: const Key('quick_add_client_submit'),
                  onPressed: _canAdd ? _add : null,
                  child: const Text('Add client'),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
