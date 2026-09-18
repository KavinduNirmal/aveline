import 'package:flutter/material.dart';

import '../../domain/client_highlight.dart';

/// Collects just enough to start a walk-in client's profile.
///
/// Returns the client to add, or `null` when the sheet is dismissed. Creating
/// them for real is the customer concierge API's job; this is the counter-side
/// shortcut the associate needs while the client is still standing there.
Future<ClientHighlight?> showQuickAddClientSheet(BuildContext context) {
  return showModalBottomSheet<ClientHighlight>(
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

    Navigator.of(context).pop(
      ClientHighlight(
        id: 'walk-in-${DateTime.now().microsecondsSinceEpoch}',
        name: name,
        // A walk-in has no history to rank yet, so they start at the entry tier
        // and the API can lift them once they have spent something.
        tier: ClientTier.level1,
        activity: 'Walk-in added at the counter.',
        hasNewActivity: true,
      ),
    );
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
