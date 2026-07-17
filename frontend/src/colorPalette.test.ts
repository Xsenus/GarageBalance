// @vitest-environment node
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

type HslColor = {
  hex: string
  hue: number
  lightness: number
  saturation: number
}

function parseHexColor(hex: string): HslColor {
  const red = Number.parseInt(hex.slice(1, 3), 16) / 255
  const green = Number.parseInt(hex.slice(3, 5), 16) / 255
  const blue = Number.parseInt(hex.slice(5, 7), 16) / 255
  const max = Math.max(red, green, blue)
  const min = Math.min(red, green, blue)
  const lightness = (max + min) / 2

  if (max === min) {
    return { hex, hue: 0, lightness, saturation: 0 }
  }

  const delta = max - min
  const saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min)
  let hue: number
  switch (max) {
    case red:
      hue = (green - blue) / delta + (green < blue ? 6 : 0)
      break
    case green:
      hue = (blue - red) / delta + 2
      break
    default:
      hue = (red - green) / delta + 4
      break
  }

  return { hex, hue: hue * 60, lightness, saturation }
}

function hasColorInRange(colors: HslColor[], fromInclusive: number, toExclusive: number) {
  return colors.some((color) => color.hue >= fromInclusive && color.hue < toExclusive)
}

describe('color palette guard', () => {
  const appCss = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8')
  const normalizedAppCss = appCss.replace(/\r\n/g, '\n')
  const hexColors = [...new Set([...appCss.matchAll(/#[0-9a-fA-F]{6}\b/g)].map((match) => match[0].toLowerCase()))]
  const expressiveColors = hexColors
    .map(parseHexColor)
    .filter((color) => color.saturation >= 0.45 && color.lightness >= 0.18 && color.lightness <= 0.92)

  it('keeps the interface palette broader than a single blue or neutral family', () => {
    expect(hexColors.length).toBeGreaterThanOrEqual(28)
    expect(expressiveColors.length).toBeGreaterThanOrEqual(12)
    expect(hasColorInRange(expressiveColors, 205, 235)).toBe(true)
    expect(hasColorInRange(expressiveColors, 135, 170)).toBe(true)
    expect(hasColorInRange(expressiveColors, 0, 12)).toBe(true)
    expect(hasColorInRange(expressiveColors, 20, 45)).toBe(true)
  })

  it('keeps semantic status colors available for success danger and warning states', () => {
    expect(appCss).toContain('#027a48')
    expect(appCss).toContain('#b42318')
    expect(appCss).toContain('#b54708')
    expect(appCss).toContain('.status-active')
    expect(appCss).toContain('.warning-text')
    expect(appCss).toContain('.danger-button')
  })

  it('keeps typography stable without viewport font scaling or negative tracking', () => {
    expect(appCss).not.toMatch(/font-size:\s*[^;]*(?:vw|vh|vmin|vmax)/i)
    expect(appCss).not.toMatch(/letter-spacing:\s*-\d/i)
    expect(normalizedAppCss).toContain('letter-spacing: 0;')
  })
})
