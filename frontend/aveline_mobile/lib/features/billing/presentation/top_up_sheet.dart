import 'dart:async';

import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../../shared/utils/currency_formatter.dart';
import '../../../../shared/utils/uuid.dart';
import '../data/payment_repository.dart';
import '../domain/top_up.dart';

/// Opens the top-up purchase sheet: the packs, the provider handoff, and the poll.
///
/// [openCheckout] is injected rather than called directly so a test can assert the handoff without
/// a platform channel, and so the shipped app can use [LaunchMode.externalApplication] while a
/// platform that cannot open a browser can still be given the URL.
Future<void> showTopUpSheet(
  BuildContext context, {
  required PaymentRepository repository,
  required String organizationId,
  String? checkoutBaseUrl,
  VoidCallback? onSettled,
}) {
  return showModalBottomSheet<void>(
    context: context,
    backgroundColor: Colors.transparent,
    isScrollControlled: true,
    builder: (_) => TopUpSheet(
      repository: repository,
      organizationId: organizationId,
      checkoutBaseUrl: checkoutBaseUrl,
      onSettled: onSettled,
      openCheckout: (url) => launchUrl(url, mode: LaunchMode.externalApplication),
    ),
  );
}

/// The purchase flow itself.
///
/// Three rules this widget exists to keep:
///
/// 1. **The server prices the pack.** The list is `GET …/blossoms/top-up-packs`; the checkout sends
///    the `skuCode` and nothing else.
/// 2. **A redirect is not settlement.** Opening `checkoutUrl` hands the customer to the provider;
///    the outcome is read back from `GET …/payment-intents/{id}` until it is terminal.
/// 3. **A retried purchase is the same operation.** The `Idempotency-Key` belongs to the pack, so
///    a retry after a refusal reuses it and the server cannot create a second charge.
class TopUpSheet extends StatefulWidget {
  const TopUpSheet({
    super.key,
    required this.repository,
    required this.organizationId,
    required this.openCheckout,
    this.checkoutBaseUrl,
    this.onSettled,
    this.pollInterval = const Duration(seconds: 2),
    this.pollTimeout = const Duration(minutes: 5),
  });

  final PaymentRepository repository;
  final String organizationId;

  /// Opens the provider's hosted page. Returns whether a browser was opened.
  final Future<bool> Function(Uri url) openCheckout;

  /// Prefix for a relative `checkoutUrl` (the Development mock's page lives on the API host).
  final String? checkoutBaseUrl;

  /// Called once the server reports a settled intent.
  final VoidCallback? onSettled;

  /// How often the poll reads the server.
  final Duration pollInterval;

  /// How long the poll keeps asking before it leaves the last server state on screen.
  final Duration pollTimeout;

  @override
  State<TopUpSheet> createState() => _TopUpSheetState();
}

class _TopUpSheetState extends State<TopUpSheet> {
  List<TopUpPack>? _packs;
  String? _packsError;
  PaymentIntent? _intent;
  String? _error;
  bool _busy = false;

  // The key belongs to the pack, so a retry of the same purchase reuses it.
  String? _keyPack;
  String? _key;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final packs = await widget.repository.fetchTopUpPacks(
        organizationId: widget.organizationId,
      );
      if (!mounted) return;
      setState(() {
        _packs = packs;
        _packsError = null;
      });
    } on PaymentUnavailable catch (error) {
      if (!mounted) return;
      setState(() => _packsError = error.message);
    } catch (_) {
      if (!mounted) return;
      setState(() => _packsError = 'Could not load the top-up packs. Try again.');
    }
  }

  String _keyFor(String skuCode) {
    if (_keyPack == skuCode && _key != null) {
      return _key!;
    }
    _keyPack = skuCode;
    _key = uuidV4();
    return _key!;
  }

  Future<void> _buy(TopUpPack pack) async {
    if (_busy) return;

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final checkout = await widget.repository.createTopUpCheckout(
        organizationId: widget.organizationId,
        skuCode: pack.skuCode,
        idempotencyKey: _keyFor(pack.skuCode),
      );

      // The attempt succeeded, so its key is spent: a later purchase mints a new one.
      _keyPack = null;
      _key = null;

      final url = checkout.checkoutUrl;
      if (url != null && url.isNotEmpty) {
        await widget.openCheckout(Uri.parse(_absolute(url)));
      }

      await _poll(checkout.paymentIntentId);
    } on PaymentUnavailable catch (error) {
      if (!mounted) return;
      setState(() => _error = error.message);
    } catch (_) {
      if (!mounted) return;
      setState(
        () => _error =
            'The request did not complete. Nothing was charged; retrying is safe.',
      );
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  Future<void> _poll(String paymentIntentId) async {
    final deadline = DateTime.now().add(widget.pollTimeout);

    while (true) {
      final intent = await widget.repository.fetchPaymentIntent(
        organizationId: widget.organizationId,
        paymentIntentId: paymentIntentId,
      );
      if (!mounted) return;
      setState(() => _intent = intent);

      if (intent.isTerminal) {
        if (intent.status == 'Succeeded') {
          widget.onSettled?.call();
        }
        return;
      }

      // Still unsettled: leave the last server state on screen rather than inventing a result.
      if (!DateTime.now().isBefore(deadline)) return;
      await Future<void>.delayed(widget.pollInterval);
      if (!mounted) return;
    }
  }

  /// The mock provider hands back a path on the API host; a real provider hands back an absolute
  /// URL. Both have to reach [Uri.parse] as something a browser can open.
  String _absolute(String url) {
    if (url.startsWith('http://') || url.startsWith('https://')) return url;
    final base = widget.checkoutBaseUrl;
    if (base == null || base.isEmpty) return url;
    return '${base.replaceAll(RegExp(r'/+$'), '')}$url';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final intent = _intent;

    return SafeArea(
      child: Container(
        decoration: BoxDecoration(
          color: scheme.surfaceContainerLowest,
          borderRadius: const BorderRadius.vertical(top: Radius.circular(20)),
        ),
        padding: const EdgeInsets.fromLTRB(20, 16, 20, 24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text('Top up Blossoms', style: theme.textTheme.titleLarge),
                ),
                IconButton(
                  onPressed: () => Navigator.of(context).maybePop(),
                  icon: const Icon(Icons.close_rounded),
                  tooltip: 'Close',
                ),
              ],
            ),
            const SizedBox(height: 4),
            Text(
              "Choose a pack and pay through the provider's secure checkout. Your balance changes "
              'only when the provider confirms the payment back to Aveline.',
              style: theme.textTheme.bodySmall?.copyWith(
                color: scheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 16),
            if (intent != null && intent.isTerminal)
              _outcome(context, intent)
            else
              _catalogue(context),
          ],
        ),
      ),
    );
  }

  Widget _catalogue(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final packs = _packs;

    if (_packsError != null) {
      return Column(
        key: const Key('top_up_packs_error'),
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(_packsError!, style: TextStyle(color: scheme.error)),
          const SizedBox(height: 12),
          OutlinedButton(
            onPressed: () {
              setState(() => _packsError = null);
              unawaited(_load());
            },
            child: const Text('Try again'),
          ),
        ],
      );
    }

    if (packs == null) {
      return const Padding(
        key: Key('top_up_loading'),
        padding: EdgeInsets.symmetric(vertical: 24),
        child: Center(child: CircularProgressIndicator()),
      );
    }

    if (packs.isEmpty) {
      return const Text('No top-up packs are configured for this boutique.');
    }

    // A `ListTile` paints its ink on the nearest `Material`; the sheet's surface is a decorated
    // container, so the list brings its own transparent one rather than losing its splashes.
    return Material(
      type: MaterialType.transparency,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          for (final pack in packs)
            ListTile(
              key: Key('top_up_pack_${pack.skuCode}'),
              contentPadding: EdgeInsets.zero,
              title: Text(
                '${groupedNumber(pack.blossomQuantity.round())} Blossoms',
              ),
              trailing: Text(
                rupees(pack.priceLkr),
                style: Theme.of(context).textTheme.titleMedium,
              ),
              onTap: _busy ? null : () => unawaited(_buy(pack)),
            ),
          if (_busy)
            const Padding(
              padding: EdgeInsets.only(top: 12),
              child: Row(
                children: [
                  SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                  SizedBox(width: 12),
                  Text("Opening the provider's checkout…"),
                ],
              ),
            ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(top: 12),
              child: Text(_error!, style: TextStyle(color: scheme.error)),
            ),
        ],
      ),
    );
  }

  Widget _outcome(BuildContext context, PaymentIntent intent) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final outcome = paymentOutcome(intent);
    final accent = switch (outcome.tone) {
      PaymentOutcomeTone.success => scheme.primary,
      PaymentOutcomeTone.error => scheme.error,
      PaymentOutcomeTone.info => scheme.onSurfaceVariant,
    };

    return Container(
      key: const Key('top_up_outcome'),
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: accent.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: accent.withValues(alpha: 0.35)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            outcome.title,
            style: theme.textTheme.titleMedium?.copyWith(color: accent),
          ),
          const SizedBox(height: 6),
          Text(outcome.detail, style: theme.textTheme.bodyMedium),
        ],
      ),
    );
  }
}
