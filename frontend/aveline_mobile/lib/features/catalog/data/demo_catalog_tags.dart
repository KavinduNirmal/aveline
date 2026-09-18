import '../domain/catalog_tag.dart';

/// Placeholder stand-in for the tags a boutique defines for its own catalog.
///
/// Every shop curates its own vocabulary, so the real row will come from the
/// shop's tag list once the inventory slice is wired. Keeping the stand-in in
/// one function makes that swap a one-line change at the call site.
List<CatalogTag> demoCatalogTags() => const [
  CatalogTag(id: 'new-in', label: 'New in'),
  CatalogTag(id: 'bridal', label: 'Bridal'),
  CatalogTag(id: 'festive', label: 'Festive'),
  CatalogTag(id: 'handloom', label: 'Handloom'),
  CatalogTag(id: 'raw-silk', label: 'Raw silk'),
  CatalogTag(id: 'evening', label: 'Evening'),
  CatalogTag(id: 'reserved', label: 'Reserved'),
  CatalogTag(id: 'bestsellers', label: 'Bestsellers'),
  CatalogTag(id: 'atelier-pick', label: 'Atelier pick'),
  CatalogTag(id: 'under-25k', label: 'Under 25k'),
];
