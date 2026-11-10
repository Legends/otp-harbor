"""Placement shared by the Desktop Store and website scanner composites."""


def desktop_demo_qr_bounds(size):
    if size != (1394, 1044):
        raise ValueError('Desktop scanner dimensions changed; review the scan target placement.')
    # Center a 360 px code (including its quiet zone) inside the captured reticle.
    return 517, 243, 877, 603
