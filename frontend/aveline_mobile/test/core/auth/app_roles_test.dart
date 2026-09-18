import 'package:aveline_mobile/core/auth/app_roles.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('AppRoles.labelFor', () {
    test('names every role the claims can carry', () {
      expect(AppRoles.labelFor(AppRoles.staff), 'Staff');
      expect(AppRoles.labelFor(AppRoles.customerRelations), 'Customer relations');
      expect(AppRoles.labelFor(AppRoles.moderator), 'Moderator');
      expect(AppRoles.labelFor(AppRoles.admin), 'Admin');
      expect(AppRoles.labelFor(AppRoles.owner), 'Owner');
      expect(AppRoles.labelFor(AppRoles.boutiqueStaff), 'Boutique staff');
      expect(AppRoles.labelFor(AppRoles.boutiqueManager), 'Boutique manager');
      expect(AppRoles.labelFor(AppRoles.boutiqueSupervisor), 'Boutique supervisor');
      expect(AppRoles.labelFor(AppRoles.boutiqueOwner), 'Boutique owner');
    });

    test('names every role the interface can be handed', () {
      // A role the server adds is visible on screen before it is named here,
      // rather than showing an empty chip or a claim id.
      expect(AppRoles.labelFor('org:boutique_apprentice'), 'org:boutique_apprentice');
      expect(AppRoles.labelFor(''), '');
    });

    test('leaves no wire value an associate would have to read', () {
      for (final role in [
        AppRoles.staff,
        AppRoles.customerRelations,
        AppRoles.moderator,
        AppRoles.admin,
        AppRoles.owner,
        AppRoles.boutiqueStaff,
        AppRoles.boutiqueManager,
        AppRoles.boutiqueSupervisor,
        AppRoles.boutiqueOwner,
      ]) {
        expect(
          AppRoles.labelFor(role),
          isNot(contains('_')),
          reason: '$role would print its wire value on the account card',
        );
      }
    });
  });
}
