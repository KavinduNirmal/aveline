import '../domain/client_highlight.dart';

/// Stand-in clients until Home is wired to the customer concierge API.
///
/// More than [ClientLinkSection.rowLimit] on purpose: the row is meant to be a
/// glance at who is active, with the rest behind "See all".
List<ClientHighlight> demoClientHighlights() => _clients;

const List<ClientHighlight> _clients = [
  ClientHighlight(
    id: 'client-eleanor',
    name: 'Eleanor Vane',
    tier: ClientTier.vip,
    activity: 'Asked for the ivory silk to be held until Friday.',
  ),
  ClientHighlight(
    id: 'client-isabella',
    name: 'Isabella Ranatunga',
    tier: ClientTier.level3,
    activity: 'Replied about the evening fitting on Thursday.',
  ),
  ClientHighlight(
    id: 'client-maya',
    name: 'Maya Tennakoon',
    tier: ClientTier.level2,
    activity: 'Waiting on the restock date for the linen shift.',
  ),
  ClientHighlight(
    id: 'client-sophia',
    name: 'Sophia Liyanage',
    tier: ClientTier.level1,
    activity: 'Sent a photo of the saree she wants matched.',
  ),
  ClientHighlight(
    id: 'client-chamari',
    name: 'Chamari Silva',
    tier: ClientTier.vip,
    activity: 'Order #AV-902 is still waiting on her approval.',
  ),
  ClientHighlight(
    id: 'client-nadia',
    name: 'Nadia Rahman',
    tier: ClientTier.level2,
    activity: 'Asked about the restock of the washed linen.',
  ),
  ClientHighlight(
    id: 'client-hiruni',
    name: 'Hiruni Bandara',
    tier: ClientTier.level3,
    activity: 'First visit at the Colombo store yesterday.',
  ),
];
