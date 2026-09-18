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
  FocusTask(
    id: 'owner-linen',
    domain: FocusDomain.logistics,
    title: 'Book in the linen delivery from Colombo Textiles',
    detail:
        'Vendor: Colombo Textiles · 12 bolts against purchase order #AV-124 come off the 9:30 AM van.',
    timeLabel: '9:30 AM',
    actionLabel: 'Sign Off',
    doneMessage: 'Linen booked in. The 9:30 AM van is clear.',
  ),
  FocusTask(
    id: 'owner-viewing',
    domain: FocusDomain.patron,
    title: 'Prepare the private viewing for Mrs. Jayasuriya',
    detail:
        'Patron: Dilani Jayasuriya · She is bringing her daughter and asked for the bridal rail to be set aside.',
    timeLabel: '2:00 PM',
    actionLabel: 'Mark ready',
    doneMessage: 'Viewing prepared. The bridal rail is set aside.',
  ),
  FocusTask(
    id: 'owner-chase',
    domain: FocusDomain.logistics,
    title: 'Chase the courier holding order #AV-914',
    detail:
        'Order #AV-914 has sat at the depot since yesterday and the patron is asking for a date.',
    timeLabel: '4:45 PM',
    actionLabel: 'Sign Off',
    doneMessage: 'Courier chased. The depot has promised a morning slot.',
  ),
  FocusTask(
    id: 'owner-alterations',
    domain: FocusDomain.wardrobe,
    title: 'Sign off the alteration list for Friday',
    detail:
        'Six pieces are with the tailor and three of them need a decision before the Friday collection.',
    timeLabel: '6:00 PM',
    actionLabel: 'Sign Off',
    doneMessage: 'Alteration list signed off. The tailor has the go-ahead.',
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
  FocusTask(
    id: 'staff-linen',
    domain: FocusDomain.logistics,
    title: 'Take in the linen delivery from Colombo Textiles',
    detail:
        'Vendor: Colombo Textiles · 12 bolts against purchase order #AV-124 come off the 11:30 AM van.',
    timeLabel: '11:30 AM',
    actionLabel: 'Sign Off',
    doneMessage: 'Linen taken in. 12 bolts are on the rail.',
  ),
  FocusTask(
    id: 'staff-returns',
    domain: FocusDomain.logistics,
    title: 'Hand the returns box to the courier',
    detail:
        'Four pieces are going back to Nuwa Silks and the courier is on the 12:15 PM round.',
    timeLabel: '12:15 PM',
    actionLabel: 'Sign Off',
    doneMessage: 'Returns handed over. Nuwa has the tracking number.',
  ),
  FocusTask(
    id: 'staff-silk',
    domain: FocusDomain.patron,
    title: 'Set out the ivory silk for Mrs. Vane',
    detail:
        'Patron: Eleanor Vane · She asked for the ivory silk to be held until Friday and wants to see it when it lands.',
    timeLabel: '2:30 PM',
    actionLabel: 'Mark ready',
    doneMessage: 'Ivory silk set out. Mrs. Vane is expected at 2:30 PM.',
  ),
  FocusTask(
    id: 'staff-steam',
    domain: FocusDomain.wardrobe,
    title: 'Steam the two held evening looks',
    detail:
        'Two looks are on hold for the 5:30 PM fitting and need the steamer before they go to the room.',
    timeLabel: '4:45 PM',
    actionLabel: 'Sign Off',
    doneMessage: 'Evening looks steamed and ready for the fitting.',
  ),
];
