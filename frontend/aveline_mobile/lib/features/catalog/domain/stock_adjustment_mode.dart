/// Operational mode for manual stock adjustment on a catalog piece.
enum StockAdjustmentMode {
  /// Shrinks the stock count by a specified amount (e.g. damaged, lost, returned to atelier, count correction).
  reduce('Reduce stock', 'Remove pieces for damage, loss, or a corrected count'),

  /// Sets the stock count to zero and marks the piece out of stock / unavailable.
  outOfStock('Mark out of stock', 'Set the count to zero and stop offering it');

  const StockAdjustmentMode(this.label, this.description);

  final String label;
  final String description;
}
