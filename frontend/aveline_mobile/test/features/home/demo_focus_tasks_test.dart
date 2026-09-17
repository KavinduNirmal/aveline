import 'package:aveline_mobile/features/home/data/demo_focus_tasks.dart';
import 'package:aveline_mobile/features/home/domain/focus_task.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('demoFocusTasks', () {
    test('gives owners and the floor different work', () {
      final owner = demoFocusTasks(isOwner: true);
      final staff = demoFocusTasks(isOwner: false);

      expect(owner, isNotEmpty);
      expect(staff, isNotEmpty);
      expect(
        owner.map((task) => task.id).toSet(),
        isNot(equals(staff.map((task) => task.id).toSet())),
      );
    });

    test('opens the owner deck with the courier approval', () {
      final first = demoFocusTasks(isOwner: true).first;

      expect(first.domain, FocusDomain.logistics);
      expect(first.actionLabel, 'Sign Off');
      expect(first.timeLabel, '3:00 PM');
    });

    test('opens the staff deck with the floor plan', () {
      expect(
        demoFocusTasks(isOwner: false).first.title,
        contains('floor plan'),
      );
    });

    test('every docket carries the copy the card renders', () {
      final all = [
        ...demoFocusTasks(isOwner: true),
        ...demoFocusTasks(isOwner: false),
      ];

      expect(all.map((task) => task.id).toSet(), hasLength(all.length));
      for (final task in all) {
        expect(task.title, isNotEmpty);
        expect(task.detail, isNotEmpty);
        expect(task.timeLabel, isNotEmpty);
        expect(task.actionLabel, isNotEmpty);
        expect(task.doneMessage, isNotEmpty);
      }
    });

    test('reuses the same list instance so a rebuild keeps the deck', () {
      expect(
        identical(demoFocusTasks(isOwner: true), demoFocusTasks(isOwner: true)),
        isTrue,
      );
    });
  });
}
