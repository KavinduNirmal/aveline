export interface InventoryItemMock {
  id: string
  name: string
  sku: string
  category: string
  color: string
  colorHex: string
  fabric: string
  style: string
  pattern?: string
  sizes: string[]
  price: number
  cost: number
  stockQuantity: number
  status: 'available' | 'reserved' | 'low_stock' | 'archived'
  imageUrl: string
  confidenceScore?: number
  description?: string
  createdAt: string
}

export interface CustomerMatchMock {
  id: string
  customerId: string
  customerName: string
  customerEmail: string
  customerAvatar?: string
  itemId: string
  itemName: string
  matchConfidence: number
  matchReason: string
  preferredColor?: string
  preferredFabric?: string
  preferredSize?: string
  employeeActed: boolean
  createdAt: string
}

export interface OutfitItemMock {
  id: string
  itemId: string
  name: string
  category: string
  price: number
  imageUrl: string
  position: 'top' | 'bottom' | 'drape' | 'accessory' | 'footwear'
  notes?: string
}

export interface OutfitCompositionMock {
  id: string
  name: string
  occasion: string
  totalPrice: number
  styleNotes: string
  heroImageUrl: string
  createdAt: string
  items: OutfitItemMock[]
}

export interface SourcingRequestMock {
  id: string
  clientName: string
  clientEmail?: string
  category: string
  color: string
  itemDescription: string
  referenceImageUrl: string
  supplierId: string
  supplierName: string
  estimatedCost: number
  proposedMarkup: number
  targetPrice: number
  status: 'pending' | 'quoted' | 'approved' | 'ordered' | 'fulfilled'
  createdAt: string
}

export interface SupplierMock {
  id: string
  name: string
  specialty: string
  contactEmail: string
  contactPhone: string
  location: string
  minimumOrder: number
  deliveryTimeDays: number
  isActive: boolean
  sampleCatalogCount: number
  catalogItems?: {
    id: string
    name: string
    category: string
    fabric: string
    wholesalePrice: number
    imageUrl: string
    inStock: boolean
  }[]
}

export const MOCK_INVENTORY: InventoryItemMock[] = [
  {
    id: 'item-1',
    name: 'Emerald Heritage Kanjeevaram Saree',
    sku: 'AVL-SAR-001',
    category: 'Sarees',
    color: 'Emerald Green',
    colorHex: '#0f5132',
    fabric: 'Pure Mulberry Silk',
    style: 'Traditional Heirloom',
    pattern: 'Gold Zari Brocade',
    sizes: ['Free Size (6.2m)'],
    price: 1450,
    cost: 650,
    stockQuantity: 4,
    status: 'available',
    imageUrl: 'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=800&q=80',
    confidenceScore: 0.96,
    description: 'Handcrafted pure mulberry silk with 24k gold electroplated zari border and delicate peacock motifs.',
    createdAt: '2026-09-01T10:00:00Z',
  },
  {
    id: 'item-2',
    name: 'Midnight Velvet Royal Sherwani',
    sku: 'AVL-SHR-042',
    category: 'Outerwear',
    color: 'Midnight Blue',
    colorHex: '#1e293b',
    fabric: 'Micro Velvet',
    style: 'Contemporary Royal',
    pattern: 'Dori & French Knot Embroidery',
    sizes: ['38', '40', '42'],
    price: 1850,
    cost: 820,
    stockQuantity: 2,
    status: 'low_stock',
    imageUrl: 'https://images.unsplash.com/photo-1594938298603-c8148c4dae35?auto=format&fit=crop&w=800&q=80',
    confidenceScore: 0.94,
    description: 'Structured tailored royal velvet jacket featuring hand-embroidered antique bullion threadwork.',
    createdAt: '2026-09-03T14:20:00Z',
  },
  {
    id: 'item-3',
    name: 'Rose Gold Chanderi Silk Lehenga',
    sku: 'AVL-LHG-019',
    category: 'Lehengas',
    color: 'Rose Gold',
    colorHex: '#b76e79',
    fabric: 'Chanderi Silk & Net',
    style: 'Festive Romantic',
    pattern: 'Gota Patti & Sequin Embellishment',
    sizes: ['36', '38'],
    price: 2100,
    cost: 950,
    stockQuantity: 1,
    status: 'low_stock',
    imageUrl: 'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b?auto=format&fit=crop&w=800&q=80',
    confidenceScore: 0.98,
    description: 'Voluminous tiered skirt complemented by a sweetheart neck blouse and scalloped organza dupatta.',
    createdAt: '2026-09-05T09:15:00Z',
  },
  {
    id: 'item-4',
    name: 'Ivory Raw Silk Angrakha Kurta',
    sku: 'AVL-KRT-108',
    category: 'Kurtas & Tunics',
    color: 'Ivory & Cream',
    colorHex: '#faf5f0',
    fabric: 'Handspun Raw Silk',
    style: 'Minimalist Festive',
    pattern: 'Tone-on-tone Chikankari',
    sizes: ['38', '40', '42', '44'],
    price: 680,
    cost: 290,
    stockQuantity: 7,
    status: 'available',
    imageUrl: 'https://images.unsplash.com/photo-1565299585323-38d6b0865b47?auto=format&fit=crop&w=800&q=80',
    confidenceScore: 0.91,
    description: 'Asymmetric crossover silhouette crafted from textured tussar raw silk with pearl button fastenings.',
    createdAt: '2026-09-07T11:45:00Z',
  },
  {
    id: 'item-5',
    name: 'Crimson Velvet Bridal Drape',
    sku: 'AVL-DRP-007',
    category: 'Drapes & Shawls',
    color: 'Deep Crimson',
    colorHex: '#800020',
    fabric: 'Silk Velvet',
    style: 'High Luxury Bridal',
    pattern: 'Zardozi Border Work',
    sizes: ['2.5m Stole'],
    price: 920,
    cost: 410,
    stockQuantity: 3,
    status: 'available',
    imageUrl: 'https://images.unsplash.com/photo-1518049362265-d5b2a6467637?auto=format&fit=crop&w=800&q=80',
    confidenceScore: 0.95,
    description: 'A plush ceremonial statement shawl finished with intricate metallic fringe and dabka embroidery.',
    createdAt: '2026-09-08T16:30:00Z',
  },
  {
    id: 'item-6',
    name: 'Sapphire Georgette Anarkali Gown',
    sku: 'AVL-GWN-089',
    category: 'Gowns',
    color: 'Sapphire Blue',
    colorHex: '#0f52ba',
    fabric: 'Pure Silk Georgette',
    style: 'Contemporary Evening',
    pattern: 'Crystal & Mirror Inlay',
    sizes: ['38', '40'],
    price: 1320,
    cost: 580,
    stockQuantity: 0,
    status: 'reserved',
    imageUrl: 'https://images.unsplash.com/photo-1566174053879-31528523f8ae?auto=format&fit=crop&w=800&q=80',
    confidenceScore: 0.93,
    description: 'Flared floor-length silhouette with sheer sleeves, mirror-work bodice, and flowing bias skirt.',
    createdAt: '2026-09-09T13:10:00Z',
  },
]

export const MOCK_CUSTOMER_MATCHES: CustomerMatchMock[] = [
  {
    id: 'match-1',
    customerId: 'cust-101',
    customerName: 'Ananya Sharma',
    customerEmail: 'ananya.s@lifestyle.com',
    customerAvatar: 'AS',
    itemId: 'item-1',
    itemName: 'Emerald Heritage Kanjeevaram Saree',
    matchConfidence: 0.94,
    matchReason: 'Prefers deep emerald & jewel tones; past purchases include handloom silk; bridal registry guest.',
    preferredColor: 'Emerald Green',
    preferredFabric: 'Mulberry Silk',
    preferredSize: 'Free Size',
    employeeActed: false,
    createdAt: '2026-09-10T08:00:00Z',
  },
  {
    id: 'match-2',
    customerId: 'cust-102',
    customerName: 'Dr. Priya Mehta',
    customerEmail: 'dr.priya@mehtaclinic.com',
    customerAvatar: 'PM',
    itemId: 'item-1',
    itemName: 'Emerald Heritage Kanjeevaram Saree',
    matchConfidence: 0.88,
    matchReason: 'Searched for traditional temple zari sarees last week; high affinity for heirloom weaves.',
    preferredColor: 'Green / Gold',
    preferredFabric: 'Silk',
    preferredSize: 'Free Size',
    employeeActed: true,
    createdAt: '2026-09-10T08:15:00Z',
  },
  {
    id: 'match-3',
    customerId: 'cust-103',
    customerName: 'Rohan Kapoor',
    customerEmail: 'rohan.k@kapoorholdings.com',
    customerAvatar: 'RK',
    itemId: 'item-2',
    itemName: 'Midnight Velvet Royal Sherwani',
    matchConfidence: 0.96,
    matchReason: 'Size 40 profile fit; requested a bespoke reception look for upcoming December gala.',
    preferredColor: 'Midnight Blue / Black',
    preferredFabric: 'Velvet',
    preferredSize: '40',
    employeeActed: false,
    createdAt: '2026-09-11T12:00:00Z',
  },
]

export const MOCK_OUTFITS: OutfitCompositionMock[] = [
  {
    id: 'outfit-1',
    name: 'Sangeet Royal Gala Ensemble',
    occasion: 'Sangeet & Reception',
    totalPrice: 2370,
    styleNotes: 'Elle suggests pairing the Emerald Kanjeevaram with a contrasting raw silk blouse and gold zari drape for maximum stage presence under warm chandeliers.',
    heroImageUrl: 'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=800&q=80',
    createdAt: '2026-09-08T10:00:00Z',
    items: [
      {
        id: 'oi-1',
        itemId: 'item-1',
        name: 'Emerald Heritage Kanjeevaram Saree',
        category: 'Sarees',
        price: 1450,
        imageUrl: 'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=800&q=80',
        position: 'drape',
        notes: 'Hero draping piece with pleated pallu',
      },
      {
        id: 'oi-2',
        itemId: 'item-5',
        name: 'Crimson Velvet Bridal Stole',
        category: 'Drapes & Shawls',
        price: 920,
        imageUrl: 'https://images.unsplash.com/photo-1518049362265-d5b2a6467637?auto=format&fit=crop&w=800&q=80',
        position: 'accessory',
        notes: 'Over-shoulder contrast accent',
      },
    ],
  },
  {
    id: 'outfit-2',
    name: 'Imperial Heritage Groom Look',
    occasion: 'Groom Royal Wedding',
    totalPrice: 2530,
    styleNotes: 'Structured silhouette combining velvet with breathable ivory raw silk underneath. Styled with antique gold buttons and minimalist churidar.',
    heroImageUrl: 'https://images.unsplash.com/photo-1594938298603-c8148c4dae35?auto=format&fit=crop&w=800&q=80',
    createdAt: '2026-09-09T14:30:00Z',
    items: [
      {
        id: 'oi-3',
        itemId: 'item-2',
        name: 'Midnight Velvet Royal Sherwani',
        category: 'Outerwear',
        price: 1850,
        imageUrl: 'https://images.unsplash.com/photo-1594938298603-c8148c4dae35?auto=format&fit=crop&w=800&q=80',
        position: 'top',
        notes: 'Primary outerwear piece',
      },
      {
        id: 'oi-4',
        itemId: 'item-4',
        name: 'Ivory Raw Silk Angrakha Kurta',
        category: 'Kurtas & Tunics',
        price: 680,
        imageUrl: 'https://images.unsplash.com/photo-1565299585323-38d6b0865b47?auto=format&fit=crop&w=800&q=80',
        position: 'top',
        notes: 'Inner layer with exposed collar details',
      },
    ],
  },
]

export const MOCK_SOURCING_REQUESTS: SourcingRequestMock[] = [
  {
    id: 'src-1',
    clientName: 'Sanjana Patel',
    clientEmail: 'sanjana@patelfamily.org',
    category: 'Lehengas',
    color: 'Dusty Lilac & Silver',
    itemDescription: 'Custom bridal lehenga with pearl tassel latkans and lightweight French lace border for a destination wedding in Lake Como.',
    referenceImageUrl: 'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b?auto=format&fit=crop&w=800&q=80',
    supplierId: 'sup-1',
    supplierName: 'Varanasi Master Handlooms',
    estimatedCost: 1100,
    proposedMarkup: 1.1,
    targetPrice: 2310,
    status: 'quoted',
    createdAt: '2026-09-06T15:00:00Z',
  },
  {
    id: 'src-2',
    clientName: 'Devika Singhania',
    clientEmail: 'devika@singhania.co',
    category: 'Sarees',
    color: 'Peacock Teal',
    itemDescription: 'Double-warp Patan Patola handwoven silk saree with geometric border work.',
    referenceImageUrl: 'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=800&q=80',
    supplierId: 'sup-2',
    supplierName: 'Heritage Wefts & Silks Co.',
    estimatedCost: 1400,
    proposedMarkup: 1.0,
    targetPrice: 2800,
    status: 'ordered',
    createdAt: '2026-09-02T11:20:00Z',
  },
  {
    id: 'src-3',
    clientName: 'Karan Mehra',
    clientEmail: 'karan@mehra.com',
    category: 'Kurtas & Tunics',
    color: 'Sage Olive',
    itemDescription: 'Hand-dyed organic muga silk bandhgala with mother-of-pearl buttons.',
    referenceImageUrl: 'https://images.unsplash.com/photo-1565299585323-38d6b0865b47?auto=format&fit=crop&w=800&q=80',
    supplierId: 'sup-3',
    supplierName: 'Atelier Jaipur Botanicals',
    estimatedCost: 350,
    proposedMarkup: 1.2,
    targetPrice: 770,
    status: 'pending',
    createdAt: '2026-09-10T09:40:00Z',
  },
]

export const MOCK_SUPPLIERS: SupplierMock[] = [
  {
    id: 'sup-1',
    name: 'Varanasi Master Handlooms',
    specialty: 'Pure Kanjeevaram & Banarasi Zari Weaving',
    contactEmail: 'orders@varanasihandlooms.in',
    contactPhone: '+91 542 228 1092',
    location: 'Varanasi, Uttar Pradesh',
    minimumOrder: 1500,
    deliveryTimeDays: 21,
    isActive: true,
    sampleCatalogCount: 148,
    catalogItems: [
      {
        id: 'cat-101',
        name: 'Royal Kadwa Zari Brocade Saree',
        category: 'Sarees',
        fabric: 'Mulberry Silk',
        wholesalePrice: 580,
        imageUrl: 'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=600&q=80',
        inStock: true,
      },
      {
        id: 'cat-102',
        name: 'Antique Gold Tissue Silk Dupatta',
        category: 'Drapes & Shawls',
        fabric: 'Tissue Silk',
        wholesalePrice: 290,
        imageUrl: 'https://images.unsplash.com/photo-1518049362265-d5b2a6467637?auto=format&fit=crop&w=600&q=80',
        inStock: true,
      },
    ],
  },
  {
    id: 'sup-2',
    name: 'Heritage Wefts & Silks Co.',
    specialty: 'Handwoven Patola & Chanderi Fabrics',
    contactEmail: 'craft@heritageweits.com',
    contactPhone: '+91 79 2658 9912',
    location: 'Ahmedabad, Gujarat',
    minimumOrder: 2000,
    deliveryTimeDays: 30,
    isActive: true,
    sampleCatalogCount: 92,
  },
  {
    id: 'sup-3',
    name: 'Atelier Jaipur Botanicals',
    specialty: 'Natural Plant Dyes & Handblock Prints',
    contactEmail: 'studio@jaipurbotanicals.art',
    contactPhone: '+91 141 239 8810',
    location: 'Jaipur, Rajasthan',
    minimumOrder: 800,
    deliveryTimeDays: 14,
    isActive: true,
    sampleCatalogCount: 64,
  },
]
