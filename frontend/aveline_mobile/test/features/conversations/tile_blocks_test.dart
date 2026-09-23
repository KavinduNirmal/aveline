import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:aveline_mobile/features/conversations/domain/tile_blocks.dart';
import 'package:flutter_test/flutter_test.dart';

/// A recommendation set is a row, not a column.
///
/// The web's `tileBlocks.test` cases, mirrored: grouping, the normalisation that
/// keeps a borrowed look photograph from being shown twice, and the definite width
/// the row's column count depends on.
void main() {
  ThreadBlock piece(String name, {String? imageUrl}) => ThreadBlock('piece', {
        'name': name,
        'price': 1000,
        'imageUrl': ?imageUrl,
      });

  ThreadBlock look({String? name, String? imageUrl, String? text}) =>
      ThreadBlock('look', {
        'name': ?name,
        'imageUrl': ?imageUrl,
        'text': ?text,
      });

  ThreadBlock text(String value) => ThreadBlock('text', {'text': value});

  group('isTileBlock', () {
    test('a piece is always a tile, photograph or not', () {
      expect(isTileBlock(piece('Ivory Organza')), isTrue);
      expect(isTileBlock(piece('Ivory Organza', imageUrl: 'https://cdn/a.jpg')), isTrue);
    });

    test('a look is a tile only while it has a photograph', () {
      expect(isTileBlock(look(name: 'Look', imageUrl: 'https://cdn/look.jpg')), isTrue);
      // Without one it is Elle's styling note about the row: prose, not a card.
      expect(isTileBlock(look(name: 'Look', text: 'Wear it with gold.')), isFalse);
    });

    test('no other block type is a tile', () {
      expect(isTileBlock(text('It comes in three sizes.')), isFalse);
      expect(isTileBlock(null), isFalse);
    });
  });

  group('withoutBorrowedLookImages', () {
    test("drops a look's photograph when it is one of the pieces' own", () {
      final blocks = [
        piece('Emerald Green Georgette Saree', imageUrl: 'https://cdn/saree.jpg'),
        look(
          name: 'Look: Boutique Collection',
          imageUrl: 'https://cdn/saree.jpg',
          text: 'Keep the silhouette clean.',
        ),
      ];

      final parsed = withoutBorrowedLookImages(blocks);

      // One photograph, on the piece: the look keeps its words and loses the copy.
      expect(parsed[0].imageUrl, 'https://cdn/saree.jpg');
      expect(parsed[1].imageUrl, isNull);
      expect(parsed[1].text, 'Keep the silhouette clean.');
      // The block it was given is untouched.
      expect(blocks[1].imageUrl, 'https://cdn/saree.jpg');
    });

    test('keeps a look photograph the pieces do not carry', () {
      final blocks = [
        piece('One', imageUrl: 'https://cdn/one.jpg'),
        look(name: 'Look', imageUrl: 'https://cdn/look.jpg'),
      ];

      expect(withoutBorrowedLookImages(blocks)[1].imageUrl, 'https://cdn/look.jpg');
    });

    test('leaves the list alone when no piece has a photograph', () {
      final blocks = [
        piece('One'),
        look(name: 'Look', imageUrl: 'https://cdn/look.jpg'),
      ];

      expect(identical(withoutBorrowedLookImages(blocks), blocks), isTrue);
    });
  });

  group('hasTileRow', () {
    test('is true for two tiles side by side', () {
      expect(hasTileRow([piece('One'), piece('Two')]), isTrue);
      expect(
        hasTileRow([
          piece('One', imageUrl: 'https://cdn/one.jpg'),
          look(name: 'Look', imageUrl: 'https://cdn/look.jpg'),
        ]),
        isTrue,
      );
    });

    test('is false for a lone tile, which stays shrink-to-fit', () {
      expect(hasTileRow([piece('Only')]), isFalse);
    });

    test("is false for a look whose photograph was borrowed, so the bubble does not widen", () {
      // The look is a note after normalisation, so there is no row to size against.
      expect(
        hasTileRow([
          piece('One', imageUrl: 'https://cdn/one.jpg'),
          look(name: 'Look', imageUrl: 'https://cdn/one.jpg', text: 'Wear it with gold.'),
        ]),
        isFalse,
      );
    });

    test('is false for prose, and for nothing at all', () {
      expect(hasTileRow([text('Three pieces match.')]), isFalse);
      expect(hasTileRow(const []), isFalse);
      expect(hasTileRow(null), isFalse);
    });
  });

  group('groupTileBlocks', () {
    test('keeps consecutive tiles in one run', () {
      final groups = groupTileBlocks([
        piece('One'),
        piece('Two'),
        piece('Three'),
      ]);

      expect(groups, hasLength(1));
      expect((groups.single as TileRun).blocks, hasLength(3));
    });

    test('starts a new run when prose separates two pieces', () {
      final groups = groupTileBlocks([
        piece('One'),
        text('It comes in three sizes.'),
        piece('Two'),
      ]);

      expect(groups, hasLength(3));
      expect((groups[0] as TileRun).blocks.single.name, 'One');
      expect((groups[1] as LoneBlock).block.type, 'text');
      expect((groups[2] as TileRun).blocks.single.name, 'Two');
    });

    test('reads a look without a photograph as a block of its own', () {
      final groups = groupTileBlocks([
        look(name: 'Look', text: 'Anchor it with muted gold.'),
      ]);

      expect(groups, hasLength(1));
      expect((groups.single as LoneBlock).block.type, 'look');
    });
  });
}
