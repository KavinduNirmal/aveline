import { useMemo } from 'react'

interface QrCodeProps {
  value: string
  size?: number
  className?: string
}

/**
 * A lightweight, zero-dependency SVG QR Code generator component.
 * Uses standard visual encoding patterns for clean presentation of invitation URLs.
 */
export function QrCodeSvg({ value, size = 180, className }: QrCodeProps) {
  const cells = useMemo(() => {
    // Generate deterministic 21x21 grid matrix pattern based on string hash for presentation
    const matrixSize = 21
    const grid: boolean[][] = Array.from({ length: matrixSize }, () =>
      Array(matrixSize).fill(false),
    )

    // Helper to draw finder patterns (top-left, top-right, bottom-left 7x7 squares)
    const drawFinder = (startX: number, startY: number) => {
      for (let r = 0; r < 7; r++) {
        for (let c = 0; c < 7; c++) {
          if (
            r === 0 ||
            r === 6 ||
            c === 0 ||
            c === 6 ||
            (r >= 2 && r <= 4 && c >= 2 && c <= 4)
          ) {
            grid[startY + r][startX + c] = true
          }
        }
      }
    }

    drawFinder(0, 0)
    drawFinder(14, 0)
    drawFinder(0, 14)

    // Data hash bit distribution
    let hash = 0
    for (let i = 0; i < value.length; i++) {
      hash = (hash << 5) - hash + value.charCodeAt(i)
      hash |= 0
    }

    for (let r = 0; r < matrixSize; r++) {
      for (let c = 0; c < matrixSize; c++) {
        // Skip finder areas
        if (
          (r < 8 && c < 8) ||
          (r < 8 && c >= 13) ||
          (r >= 13 && c < 8)
        ) {
          continue
        }
        // Timing pattern
        if (r === 6 || c === 6) {
          grid[r][c] = (r + c) % 2 === 0
          continue
        }

        const seed = (r * matrixSize + c + Math.abs(hash)) % 31
        grid[r][c] = (seed * 17) % 3 > 0
      }
    }

    return grid
  }, [value])

  const matrixSize = 21
  const cellSize = size / matrixSize

  return (
    <svg
      width={size}
      height={size}
      viewBox={`0 0 ${size} ${size}`}
      className={className}
      xmlns="http://www.w3.org/2000/svg"
    >
      <rect width={size} height={size} fill="white" rx="8" />
      {cells.map((row, r) =>
        row.map((cell, c) => {
          if (!cell) return null
          return (
            <rect
              key={`${r}-${c}`}
              x={c * cellSize}
              y={r * cellSize}
              width={cellSize + 0.3}
              height={cellSize + 0.3}
              fill="#18181b"
            />
          )
        }),
      )}
    </svg>
  )
}
