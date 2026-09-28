import 'package:aveline_mobile/features/catalog/domain/sourcing_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_request.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_status.dart';
import 'package:aveline_mobile/features/catalog/domain/supplier.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('SourcingStatus', () {
    test('parses wire representations cleanly', () {
      expect(SourcingStatus.fromWire('pending'), SourcingStatus.pending);
      expect(SourcingStatus.fromWire('quoted'), SourcingStatus.quoted);
      expect(SourcingStatus.fromWire('approved'), SourcingStatus.approved);
      expect(SourcingStatus.fromWire('ordered'), SourcingStatus.ordered);
      expect(SourcingStatus.fromWire('fulfilled'), SourcingStatus.fulfilled);
      expect(SourcingStatus.fromWire('archived'), SourcingStatus.archived);
      expect(SourcingStatus.fromWire(null), SourcingStatus.pending);
      expect(SourcingStatus.fromWire('unknown'), SourcingStatus.pending);
    });

    test('exposes luxury labels and badge colors', () {
      expect(SourcingStatus.pending.label, 'Pending Quote');
      expect(SourcingStatus.quoted.label, 'Quoted by Atelier');
      expect(SourcingStatus.approved.label, 'Approved');
      expect(SourcingStatus.ordered.label, 'Ordered from Atelier');
      expect(SourcingStatus.fulfilled.label, 'Fulfilled');
      expect(SourcingStatus.archived.label, 'Archived');

      expect(SourcingStatus.pending.isActive, isTrue);
      expect(SourcingStatus.archived.isActive, isFalse);
    });
  });

  group('Supplier', () {
    test('parses JSON properly', () {
      final json = {
        'id': 'sup-1',
        'name': 'Varanasi Silk Works',
        'location': 'Varanasi, UP',
        'specialty': 'Pure Katan Silk & Brocade',
        'rating': 4.9,
        'contactEmail': 'atelier@varanasi.craft',
        'leadTimeDays': 21,
      };

      final supplier = Supplier.fromJson(json);
      expect(supplier.id, 'sup-1');
      expect(supplier.name, 'Varanasi Silk Works');
      expect(supplier.location, 'Varanasi, UP');
      expect(supplier.rating, 4.9);
      expect(supplier.leadTimeDays, 21);
    });
  });

  group('SourcingRequest', () {
    test('parses JSON with calculated margins', () {
      final json = {
        'id': 'src-101',
        'clientName': 'Mrs. Radhika Merchant',
        'category': 'Lehengas',
        'color': 'Peacock Emerald',
        'itemDescription': 'Custom bridal lehenga with antique gold zardozi embroidery.',
        'referenceImageUrl': 'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b',
        'supplierId': 'sup-1',
        'supplierName': 'Varanasi Silk Works',
        'estimatedCost': 950.0,
        'targetPrice': 2000.0,
        'status': 'pending',
        'createdAt': '2026-09-24T12:00:00Z',
        'urgency': 'high',
      };

      final ticket = SourcingRequest.fromJson(json);
      expect(ticket.id, 'src-101');
      expect(ticket.clientName, 'Mrs. Radhika Merchant');
      expect(ticket.category, 'Lehengas');
      expect(ticket.estimatedCost, 950.0);
      expect(ticket.targetPrice, 2000.0);
      expect(ticket.marginAmount, 1050.0);
      // Margin = ((2000 - 950) / 950) * 100 = ~110.526%
      expect(ticket.marginPercentage.toStringAsFixed(1), '110.5');
      expect(ticket.status, SourcingStatus.pending);
      expect(ticket.isOpen, isTrue);
      expect(ticket.isArchived, isFalse);
    });

    test('handles zero cost without division by zero error', () {
      const ticket = SourcingRequest(
        id: 'src-102',
        clientName: 'Client',
        category: 'Sarees',
        color: 'Gold',
        itemDescription: 'Silk drape',
        supplierId: 'sup-1',
        supplierName: 'Atelier',
        estimatedCost: 0,
        targetPrice: 500,
      );

      expect(ticket.marginPercentage, 0.0);
      expect(ticket.marginAmount, 500.0);
    });

    test('serializes to JSON correctly', () {
      final ticket = SourcingRequest(
        id: 'src-103',
        clientName: 'Ananya Birla',
        category: 'Sherwanis',
        color: 'Ivory Cream',
        itemDescription: 'Raw silk sherwani',
        supplierId: 'sup-2',
        supplierName: 'Jaipur Handlooms',
        estimatedCost: 600.0,
        targetPrice: 1500.0,
        status: SourcingStatus.approved,
        createdAt: DateTime.utc(2026, 9, 24),
      );

      final json = ticket.toJson();
      expect(json['id'], 'src-103');
      expect(json['clientName'], 'Ananya Birla');
      expect(json['status'], 'approved');
      expect(json['targetPrice'], 1500.0);
    });
  });

  group('Sourcing Payloads', () {
    test('CreateSourcingRequestPayload calculates markup properly', () {
      const payload = CreateSourcingRequestPayload(
        clientName: 'Devraj Rajput',
        category: 'Bandhgalas',
        color: 'Royal Navy',
        description: 'Velvet bespoke suit with crested buttons',
        supplierId: 'sup-3',
        estimatedCost: 800.0,
        targetPrice: 1800.0,
        urgency: 'urgent',
      );

      final json = payload.toJson(organizationId: 'org-123');
      expect(json['organizationId'], 'org-123');
      expect(json['clientName'], 'Devraj Rajput');
      expect(json['proposedMarkup'], 1.25);
    });

    test('UpdateSourcingStatusPayload serializes correctly', () {
      const payload = UpdateSourcingStatusPayload(
        status: SourcingStatus.ordered,
        notes: 'Fabric dyed and weave started in atelier.',
      );

      final json = payload.toJson();
      expect(json['status'], 'ordered');
      expect(json['notes'], 'Fabric dyed and weave started in atelier.');
    });
  });
}
