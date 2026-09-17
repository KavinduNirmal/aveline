import '../domain/focus_task.dart';

/// Stand-in focus deck until Home is wired to the API.
///
/// Owners get approval-shaped work, everyone else gets floor-shaped work, so
/// the deck reads as "yours" on either side of the boutique. Every task is an
/// acknowledgement or a sign-off, which keeps the local completion state honest
/// while there is no backend behind it.
List<FocusTask> demoFocusTasks({required bool isOwner}) =>
    isOwner ? _ownerTasks : _staffTasks;

const List<FocusTask> _ownerTasks = [
  FocusTask(
    id: 'owner-courier',
    domain: FocusDomain.logistics,
    title: 'Approve delivery courier for Mrs. Silva',
    detail:
        'Patron: Maria Silva · Delivery Waybill #AV-881. Bespoke silk collection dispatch confirmation.',
    timeLabel: '3:00 PM',
    actionLabel: 'Sign Off',
    doneMessage: 'Courier approved. The dispatch leaves the atelier today.',
  ),
  FocusTask(
    id: 'owner-quotation',
    domain: FocusDomain.commerce,
    title: 'Confirm the quotation for Ranmali Fernando',
    detail:
        'Patron: Ranmali Fernando · Quote #AV-902 covers two evening looks and is waiting on your approval.',
    timeLabel: '12:30 PM',
    actionLabel: 'Approve',
    doneMessage: 'Quotation approved. Ranmali will hear from you in Messages.',
  ),
  FocusTask(
    id: 'owner-intake',
    domain: FocusDomain.wardrobe,
    title: 'Review the silk intake from Nuwa',
    detail:
        'Vendor: Nuwa Silks · 24 pieces booked against purchase order #AV-118 are waiting to be checked in.',
    timeLabel: '4:15 PM',
    actionLabel: 'Sign Off',
    doneMessage: 'Intake signed off. All 24 pieces are sellable.',
  ),
  FocusTask(
    id: 'owner-fitting',
    domain: FocusDomain.patron,
    title: 'Set the fitting room for Mrs. Perera',
    detail:
        'Patron: Anoma Perera · Two held evening looks need the room, the steamer, and a note from her last visit.',
    timeLabel: '5:30 PM',
    actionLabel: 'Mark ready',
    doneMessage: 'Fitting room ready. Mrs. Perera arrives at 5:30 PM.',
  ),
];

const List<FocusTask> _staffTasks = [
  FocusTask(
    id: 'staff-floor-plan',
    domain: FocusDomain.logistics,
    title: 'Acknowledge the floor plan for today',
    detail:
        'Manager note · Two appointments, one intake, and a 1:00 PM handover for the evening shift.',
    timeLabel: '10:00 AM',
    actionLabel: 'Acknowledge',
    doneMessage: 'Floor plan acknowledged. It is pinned to the Salon for the shift.',
  ),
  FocusTask(
    id: 'staff-appointment',
    domain: FocusDomain.patron,
    title: 'Prepare for the 3:00 PM appointment',
    detail:
        'Patron: Maria Silva · She asked for the silk edit, and her last visit is on file in Customers.',
    timeLabel: '3:00 PM',
    actionLabel: 'Mark ready',
    doneMessage: 'Preparation marked ready. Maria arrives at 3:00 PM.',
  ),
  FocusTask(
    id: 'staff-intake',
    domain: FocusDomain.wardrobe,
    title: 'Count in the silk intake from Nuwa',
    detail:
        'Vendor: Nuwa Silks · 24 pieces against purchase order #AV-118 need to be checked and tagged.',
    timeLabel: '4:15 PM',
    actionLabel: 'Sign Off',
    doneMessage: 'Intake signed off. 24 pieces are tagged and on the floor.',
  ),
  FocusTask(
    id: 'staff-handover',
    domain: FocusDomain.commerce,
    title: 'Review the handover note for the evening shift',
    detail:
        'Colleague: Nadia · The evening shift takes over at 6:00 PM and needs the open items from today.',
    timeLabel: '5:45 PM',
    actionLabel: 'Sign Off',
    doneMessage: 'Handover reviewed. The evening shift has the full picture.',
  ),
];
