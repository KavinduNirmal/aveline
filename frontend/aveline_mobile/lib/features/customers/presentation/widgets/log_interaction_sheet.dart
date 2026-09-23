import 'package:flutter/material.dart';

import '../../../../shared/utils/date_formatter.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/filter_pill.dart';
import '../../domain/customer.dart';
import '../../domain/customer_detail.dart';

/// Luxury bottom sheet modal for logging a new customer interaction or counter visit.
class LogInteractionSheet extends StatefulWidget {
  const LogInteractionSheet({
    super.key,
    required this.customer,
    required this.onSubmit,
  });

  final Customer customer;
  final Future<void> Function(RecordInteractionRequest request) onSubmit;

  static Future<void> show({
    required BuildContext context,
    required Customer customer,
    required Future<void> Function(RecordInteractionRequest request) onSubmit,
  }) {
    return showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (context) => LogInteractionSheet(
        customer: customer,
        onSubmit: onSubmit,
      ),
    );
  }

  @override
  State<LogInteractionSheet> createState() => _LogInteractionSheetState();
}

class _LogInteractionSheetState extends State<LogInteractionSheet> {
  final TextEditingController _noteController = TextEditingController();
  final TextEditingController _amountController = TextEditingController();
  final TextEditingController _tagController = TextEditingController();

  InteractionChannel _channel = InteractionChannel.inPerson;
  InteractionDirection _direction = InteractionDirection.inbound;
  DateTime _occurredAt = DateTime.now();
  final List<String> _tags = [];
  bool _isSaving = false;

  @override
  void dispose() {
    _noteController.dispose();
    _amountController.dispose();
    _tagController.dispose();
    super.dispose();
  }

  bool get _isVisit =>
      _channel == InteractionChannel.inPerson &&
      _direction == InteractionDirection.inbound;

  Future<void> _pickDateTime() async {
    final now = DateTime.now();
    final pickedDate = await showDatePicker(
      context: context,
      initialDate: _occurredAt,
      firstDate: now.subtract(const Duration(days: 30)),
      lastDate: now.add(const Duration(minutes: 5)),
    );
    if (pickedDate == null || !mounted) return;

    final pickedTime = await showTimePicker(
      context: context,
      initialTime: TimeOfDay.fromDateTime(_occurredAt),
    );
    if (pickedTime == null || !mounted) return;

    setState(() {
      _occurredAt = DateTime(
        pickedDate.year,
        pickedDate.month,
        pickedDate.day,
        pickedTime.hour,
        pickedTime.minute,
      );
    });
  }

  void _addTag() {
    final raw = _tagController.text.trim();
    if (raw.isNotEmpty && !_tags.contains(raw)) {
      setState(() {
        _tags.add(raw);
        _tagController.clear();
      });
    }
  }

  void _removeTag(String tag) {
    setState(() => _tags.remove(tag));
  }

  Future<void> _submit() async {
    final note = _noteController.text.trim();
    final amountText = _amountController.text.trim();
    double? parsedAmount;

    if (amountText.isNotEmpty) {
      parsedAmount = double.tryParse(amountText.replaceAll(',', ''));
      if (parsedAmount == null || parsedAmount < 0) {
        AppToast.show(context, 'Please enter a valid positive amount.');
        return;
      }
    }

    if (note.isEmpty && parsedAmount == null) {
      AppToast.show(context, 'Please enter a discussion note or amount.');
      return;
    }

    setState(() => _isSaving = true);
    try {
      final request = RecordInteractionRequest(
        occurredAtUtc: _occurredAt.toUtc(),
        channel: _channel,
        direction: _direction,
        note: note.isNotEmpty ? note : null,
        purchaseTotal: parsedAmount,
        tags: List<String>.from(_tags),
      );

      await widget.onSubmit(request);
      if (mounted) {
        Navigator.of(context).pop();
        AppToast.show(
          context,
          _isVisit
              ? 'Visit recorded for ${widget.customer.displayName}.'
              : 'Interaction recorded for ${widget.customer.displayName}.',
        );
      }
    } catch (e) {
      if (mounted) {
        AppToast.show(context, 'Failed to record interaction: $e');
      }
    } finally {
      if (mounted) {
        setState(() => _isSaving = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final bottomInset = MediaQuery.viewInsetsOf(context).bottom;

    return Container(
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(28)),
        boxShadow: [
          BoxShadow(
            color: const Color(0xFF8B2E42).withValues(alpha: 0.12),
            blurRadius: 32,
            offset: const Offset(0, -8),
          ),
        ],
      ),
      child: SafeArea(
        top: false,
        child: Padding(
          padding: EdgeInsets.fromLTRB(20, 16, 20, 20 + bottomInset),
          child: SingleChildScrollView(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                // Top handle
                Center(
                  child: Container(
                    width: 40,
                    height: 4,
                    decoration: BoxDecoration(
                      color: scheme.outlineVariant.withValues(alpha: 0.6),
                      borderRadius: BorderRadius.circular(999),
                    ),
                  ),
                ),
                const SizedBox(height: 16),
                // Title
                Row(
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            'Log Interaction',
                            style: theme.textTheme.headlineSmall?.copyWith(
                              color: scheme.onSurface,
                            ),
                          ),
                          const SizedBox(height: 2),
                          Text(
                            'For ${widget.customer.displayName}',
                            style: theme.textTheme.bodyMedium?.copyWith(
                              color: scheme.onSurfaceVariant,
                            ),
                          ),
                        ],
                      ),
                    ),
                    IconButton(
                      icon: const Icon(Icons.close_rounded),
                      onPressed: () => Navigator.of(context).pop(),
                    ),
                  ],
                ),
                const SizedBox(height: 16),
                // Channel Selection
                Text(
                  'CHANNEL',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                    letterSpacing: 1.1,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 8),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (final ch in InteractionChannel.values)
                      FilterPill(
                        label: ch.label,
                        selected: _channel == ch,
                        onTap: () => setState(() => _channel = ch),
                      ),
                  ],
                ),
                const SizedBox(height: 16),
                // Direction Selection
                Text(
                  'DIRECTION',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                    letterSpacing: 1.1,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 8),
                Row(
                  children: [
                    Expanded(
                      child: FilterPill(
                        label: 'Inbound (From Client)',
                        selected: _direction == InteractionDirection.inbound,
                        onTap: () => setState(
                          () => _direction = InteractionDirection.inbound,
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: FilterPill(
                        label: 'Outbound (From Staff)',
                        selected: _direction == InteractionDirection.outbound,
                        onTap: () => setState(
                          () => _direction = InteractionDirection.outbound,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 16),
                // When / DateTime Selector
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Text(
                      'OCCURRED AT',
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: scheme.onSurfaceVariant,
                        letterSpacing: 1.1,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const SizedBox(width: 8),
                    Flexible(
                      child: InkWell(
                        onTap: _pickDateTime,
                        borderRadius: BorderRadius.circular(8),
                        child: Padding(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 8,
                            vertical: 4,
                          ),
                          child: Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Icon(
                                Icons.calendar_today_outlined,
                                size: 14,
                                color: scheme.primary,
                              ),
                              const SizedBox(width: 6),
                              Flexible(
                                child: Text(
                                  relativeDayAndTime(_occurredAt),
                                  style: theme.textTheme.labelMedium?.copyWith(
                                    color: scheme.primary,
                                    fontWeight: FontWeight.w600,
                                  ),
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 16),
                // Purchase Total / Amount Taken
                Text(
                  'AMOUNT TAKEN (OPTIONAL)',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                    letterSpacing: 1.1,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 6),
                TextField(
                  controller: _amountController,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  decoration: InputDecoration(
                    hintText: 'e.g. 45000 (Added to lifetime spend)',
                    prefixText: 'LKR ',
                    prefixStyle: theme.textTheme.bodyMedium?.copyWith(
                      color: scheme.onSurface,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
                const SizedBox(height: 16),
                // Discussion Note
                Text(
                  'DISCUSSION / INTERACTION NOTES',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                    letterSpacing: 1.1,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 6),
                TextField(
                  controller: _noteController,
                  maxLines: 3,
                  decoration: const InputDecoration(
                    hintText:
                        'Garments inspected, sizing feedback, color preferences, or fitting requests...',
                  ),
                ),
                const SizedBox(height: 16),
                // Tags
                Text(
                  'TAGS & OCCASIONS',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                    letterSpacing: 1.1,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 6),
                Row(
                  children: [
                    Expanded(
                      child: TextField(
                        controller: _tagController,
                        onSubmitted: (_) => _addTag(),
                        decoration: const InputDecoration(
                          hintText: 'Add tag (e.g. Wedding, Raw Silk)',
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),
                    IconButton.filledTonal(
                      onPressed: _addTag,
                      icon: const Icon(Icons.add, size: 20),
                    ),
                  ],
                ),
                if (_tags.isNotEmpty) ...[
                  const SizedBox(height: 8),
                  Wrap(
                    spacing: 6,
                    runSpacing: 6,
                    children: [
                      for (final tag in _tags)
                        Chip(
                          label: Text(tag),
                          onDeleted: () => _removeTag(tag),
                          deleteIconColor: scheme.onSurfaceVariant,
                        ),
                    ],
                  ),
                ],
                const SizedBox(height: 24),
                // Submit Button
                SizedBox(
                  width: double.infinity,
                  height: 52,
                  child: FilledButton(
                    onPressed: _isSaving ? null : _submit,
                    child: _isSaving
                        ? const SizedBox(
                            width: 22,
                            height: 22,
                            child: CircularProgressIndicator(
                              strokeWidth: 2,
                              color: Colors.white,
                            ),
                          )
                        : Text(
                            _isVisit
                                ? 'Record In-Person Visit'
                                : 'Record Interaction',
                            style: theme.textTheme.labelLarge?.copyWith(
                              color: Colors.white,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
