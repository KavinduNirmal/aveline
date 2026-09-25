import 'package:flutter_test/flutter_test.dart';
import 'package:aveline_mobile/features/catalog/domain/customer_match.dart';

void main() {
  group('CustomerMatch', () {
    test('parses from valid JSON and calculates percentages correctly', () {
      final json = {
        'id': 'match-1',
        'customerId': 'cust-101',
        'customerName': 'Maya Lin',
        'customerEmail': 'maya.lin@vip.aveline.com',
        'customerAvatar': 'https://images.unsplash.com/avatar1',
        'matchConfidence': 0.94,
        'matchReason': 'Client frequently orders emerald raw silk in size 38.',
        'preferredColor': 'Emerald Green',
        'preferredFabric': 'Raw Silk',
        'preferredSize': '38',
        'employeeActed': false,
      };

      final match = CustomerMatch.fromJson(json);

      expect(match.id, 'match-1');
      expect(match.customerId, 'cust-101');
      expect(match.customerName, 'Maya Lin');
      expect(match.customerEmail, 'maya.lin@vip.aveline.com');
      expect(match.matchConfidence, 0.94);
      expect(match.matchPercentage, 94);
      expect(match.matchScoreLabel, '94% Match');
      expect(match.scoreProgress, 0.94);
      expect(match.preferredColor, 'Emerald Green');
      expect(match.preferredFabric, 'Raw Silk');
      expect(match.preferredSize, '38');
      expect(match.employeeActed, isFalse);
    });

    test('normalizes raw integer percentage scores over 1.0', () {
      final json = {
        'id': 'match-2',
        'customerId': 'cust-102',
        'customerName': 'Priya Sharma',
        'matchConfidence': 88,
        'matchReason': 'Past bridal reception preference.',
      };

      final match = CustomerMatch.fromJson(json);

      expect(match.matchConfidence, 0.88);
      expect(match.matchPercentage, 88);
      expect(match.matchScoreLabel, '88% Match');
    });

    test('copyWith updates fields correctly', () {
      const initial = CustomerMatch(
        id: 'match-3',
        customerId: 'cust-103',
        customerName: 'Elena Rostova',
        customerEmail: 'elena@vip.com',
        matchConfidence: 0.82,
        matchReason: 'Classic luxury styling match.',
        employeeActed: false,
      );

      final updated = initial.copyWith(employeeActed: true);

      expect(updated.employeeActed, isTrue);
      expect(updated.id, 'match-3');
      expect(updated.customerName, 'Elena Rostova');
    });

    test('toJson serializes all fields properly', () {
      const match = CustomerMatch(
        id: 'match-4',
        customerId: 'cust-104',
        customerName: 'Sanjana Patel',
        customerEmail: 'sanjana@vip.com',
        matchConfidence: 0.91,
        matchReason: 'Loves Kanjeevaram weaves.',
        employeeActed: true,
      );

      final json = match.toJson();

      expect(json['id'], 'match-4');
      expect(json['customerId'], 'cust-104');
      expect(json['customerName'], 'Sanjana Patel');
      expect(json['matchConfidence'], 0.91);
      expect(json['employeeActed'], isTrue);
    });
  });
}
