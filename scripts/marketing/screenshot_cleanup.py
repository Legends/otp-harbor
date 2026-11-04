"""Lossless app-region extraction shared by review and marketing generation."""

from PIL import Image, ImageDraw

DESKTOP_EDGE = 3
DESKTOP_CORNER_RADIUS = 20
ANDROID_TOP = 100
ANDROID_BOTTOM = 80


def rounded_alpha(size, radius=DESKTOP_CORNER_RADIUS):
    factor = 4
    mask = Image.new('L', (size[0] * factor, size[1] * factor))
    ImageDraw.Draw(mask).rounded_rectangle(
        (0, 0, mask.width - 1, mask.height - 1),
        radius=radius * factor,
        fill=255,
    )
    return mask.resize(size, Image.Resampling.LANCZOS)


def clean_desktop(image):
    crop = (DESKTOP_EDGE, DESKTOP_EDGE, image.width - DESKTOP_EDGE, image.height - DESKTOP_EDGE)
    cleaned = image.crop(crop).convert('RGBA')
    cleaned.putalpha(rounded_alpha(cleaned.size))
    return cleaned, crop


def clean_android(image):
    crop = (0, ANDROID_TOP, image.width, image.height - ANDROID_BOTTOM)
    return image.crop(crop).convert('RGB'), crop
