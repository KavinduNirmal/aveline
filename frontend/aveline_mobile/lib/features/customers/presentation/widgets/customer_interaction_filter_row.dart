import 'package:flutter/material.dart';

import '../../../../shared/widgets/filter_pill.dart';
import '../../domain/customer_detail.dart';

/// Horizontally scrolling channel and direction filter selector.
class CustomerInteractionFilterRow extends StatelessWidget {
  const CustomerInteractionFilterRow({
    super.key,
    required this.selectedChannel,
    required this.selectedDirection,
    required this.onChannelSelected,
    required this.onDirectionSelected,
  });

  final InteractionChannel? selectedChannel;
  final InteractionDirection? selectedDirection;
  final ValueChanged<InteractionChannel?> onChannelSelected;
  final ValueChanged<InteractionDirection?> onDirectionSelected;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          physics: const BouncingScrollPhysics(),
          child: Row(
            children: [
              FilterPill(
                label: 'All Channels',
                selected: selectedChannel == null,
                onTap: () => onChannelSelected(null),
              ),
              const SizedBox(width: 8),
              for (final channel in InteractionChannel.values) ...[
                FilterPill(
                  label: channel.label,
                  selected: selectedChannel == channel,
                  onTap: () => onChannelSelected(
                    selectedChannel == channel ? null : channel,
                  ),
                ),
                const SizedBox(width: 8),
              ],
            ],
          ),
        ),
        const SizedBox(height: 10),
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          physics: const BouncingScrollPhysics(),
          child: Row(
            children: [
              FilterPill(
                label: 'All Directions',
                selected: selectedDirection == null,
                onTap: () => onDirectionSelected(null),
              ),
              const SizedBox(width: 8),
              FilterPill(
                label: 'Inbound (From Client)',
                selected: selectedDirection == InteractionDirection.inbound,
                onTap: () => onDirectionSelected(
                  selectedDirection == InteractionDirection.inbound
                      ? null
                      : InteractionDirection.inbound,
                ),
              ),
              const SizedBox(width: 8),
              FilterPill(
                label: 'Outbound (From Boutique)',
                selected: selectedDirection == InteractionDirection.outbound,
                onTap: () => onDirectionSelected(
                  selectedDirection == InteractionDirection.outbound
                      ? null
                      : InteractionDirection.outbound,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
