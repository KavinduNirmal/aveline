import 'package:flutter/material.dart';

import '../../../../core/theme/channel_colors.dart';
import '../../../../shared/utils/phone_formatter.dart';
import 'whatsapp_mark.dart';

/// The customer's handle as a person reads it.
///
/// WhatsApp hands the number over as a bare `94763475058`. A Sri Lankan one gets the
/// convention the rest of the app already spaces numbers with; anything else is left
/// exactly as the channel gave it, because forcing the local country code onto a
/// foreign number would invent an address the customer does not have.
String customerHandle(String handle) {
  final digits = handle.replaceAll(RegExp(r'\D'), '');
  final isSriLankan =
      (digits.length == 11 && digits.startsWith('94')) ||
      (digits.length == 10 && digits.startsWith('0'));
  return isSriLankan ? formatLkPhone(handle) : handle;
}

/// An inbound customer message, drawn as the channel it arrived on.
///
/// This is a **message surface, not a card inside one**. The web nests its channel
/// figure in the bubble; on a phone that reads as a card inside a card, which is two
/// frames for one message and buries the customer's words a level deeper than
/// everyone else's. So the surface is what the message is drawn on: the mark and the
/// handle name the channel, and the words sit on the channel's own canvas in an
/// inbound bubble. There is no bubble around it.
///
/// It also states its own ink whatever the thread put around it — a tint of the
/// associate's bubble would read as Aveline talking, and the whole point of the block
/// is that she is not.
class ClientChannelSurface extends StatelessWidget {
  const ClientChannelSurface({
    super.key,
    required this.text,
    this.handle,
    this.borderRadius,
    this.child,
  });

  /// The customer's own words. Kept as plain text: a mention is the staff's
  /// affordance, and a `@` a customer types is a handle they wrote.
  final String text;

  /// The channel handle the message arrived from, when it carried one.
  final String? handle;

  /// The message's outer radii. A thread flattens the corner nearest its own side.
  final BorderRadius? borderRadius;

  /// Anything else the message carries, drawn under the words on the canvas.
  final Widget? child;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final channel = ChannelColors.of(context);
    final trimmed = handle?.trim() ?? '';

    return Container(
      key: const Key('client_channel_surface'),
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: borderRadius ?? BorderRadius.circular(16),
        border: Border.all(color: channel.whatsapp.withValues(alpha: 0.25)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          _Header(handle: trimmed, channel: channel),
          Container(
            width: double.infinity,
            color: channel.whatsappCanvas,
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
            child: LayoutBuilder(
              builder: (context, constraints) => Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Align(
                    alignment: Alignment.centerLeft,
                    child: ConstrainedBox(
                      // The bubble hugs what the customer actually wrote: a one-line
                      // question in a full-width plate reads as an empty form, which
                      // is the opposite of a chat.
                      constraints: BoxConstraints(
                        maxWidth: constraints.maxWidth * 0.95,
                      ),
                      child: _InboundBubble(text: text),
                    ),
                  ),
                  if (child != null) ...[
                    const SizedBox(height: 8),
                    child!,
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// The channel header: the mark, the customer, and the channel's name.
class _Header extends StatelessWidget {
  const _Header({required this.handle, required this.channel});

  final String handle;
  final ChannelColors channel;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      decoration: BoxDecoration(
        color: channel.whatsapp.withValues(alpha: 0.10),
        border: Border(
          bottom: BorderSide(color: channel.whatsapp.withValues(alpha: 0.20)),
        ),
      ),
      child: Row(
        children: [
          Container(
            key: const Key('client_channel_mark'),
            width: 28,
            height: 28,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: channel.whatsapp,
            ),
            child: WhatsAppMark(
              color: channel.whatsappForeground,
              size: 16,
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  'CUSTOMER',
                  style: Theme.of(context).textTheme.labelSmall?.copyWith(
                        fontSize: 10,
                        fontWeight: FontWeight.w600,
                        color: scheme.onSurfaceVariant,
                        letterSpacing: 1.4,
                      ),
                ),
                if (handle.isNotEmpty)
                  Text(
                    customerHandle(handle),
                    key: const Key('client_channel_handle'),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                          fontSize: 13,
                          height: 1.4,
                          fontWeight: FontWeight.w500,
                          color: scheme.onSurface,
                        ),
                  ),
              ],
            ),
          ),
          const SizedBox(width: 10),
          // The channel is named in ink, not in the brand green: white on the mark
          // fails contrast at label size, and the mark beside it is already the brand.
          Container(
            key: const Key('client_channel_label'),
            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
            decoration: BoxDecoration(
              color: channel.whatsapp.withValues(alpha: 0.15),
              borderRadius: BorderRadius.circular(999),
            ),
            child: Text(
              'WhatsApp',
              style: Theme.of(context).textTheme.labelSmall?.copyWith(
                    fontSize: 10,
                    fontWeight: FontWeight.w600,
                    color: channel.whatsappDeep,
                  ),
            ),
          ),
        ],
      ),
    );
  }
}

/// The inbound chat bubble, with the tail that points back at the channel.
class _InboundBubble extends StatelessWidget {
  const _InboundBubble({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Row(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // The tail: a right-pointing triangle in the bubble's own fill, so it matches
        // the bubble in either theme without carrying a border of its own.
        CustomPaint(
          size: const Size(7, 14),
          painter: _InboundTailPainter(color: scheme.surfaceContainerLowest),
        ),
        Flexible(
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
            decoration: BoxDecoration(
              color: scheme.surfaceContainerLowest,
              borderRadius: const BorderRadius.only(
                topLeft: Radius.circular(2),
                topRight: Radius.circular(16),
                bottomLeft: Radius.circular(16),
                bottomRight: Radius.circular(16),
              ),
            ),
            child: Text(
              text,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    color: scheme.onSurface,
                  ),
            ),
          ),
        ),
      ],
    );
  }
}

/// The inbound chat tail.
class _InboundTailPainter extends CustomPainter {
  const _InboundTailPainter({required this.color});

  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()..color = color;
    final path = Path()
      ..moveTo(size.width, 0)
      ..lineTo(0, 0)
      ..lineTo(size.width, size.height)
      ..close();
    canvas.drawPath(path, paint);
  }

  @override
  bool shouldRepaint(_InboundTailPainter oldDelegate) =>
      oldDelegate.color != color;
}
