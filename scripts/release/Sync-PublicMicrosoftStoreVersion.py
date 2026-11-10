#!/usr/bin/env python3
"""Refresh confirmed Store metadata from Microsoft's public retail package catalog."""

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import subprocess
import urllib.request

PRODUCT_ID = "9P31KH5L924P"
IDENTITY = "Legends77.OTPHarbor"
PUBLISHER_ID = "bev6xjqt2jr8t"
CATALOG_URL = (
    f"https://displaycatalog.mp.microsoft.com/v7.0/products/{PRODUCT_ID}"
    "?languages=en-us&market=US&fieldsTemplate=Details"
)
VERSION = r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
PACKAGE = re.compile(rf"^{re.escape(IDENTITY)}_({VERSION})_x64__{PUBLISHER_ID}$")
ROOT = Path(__file__).resolve().parents[2]


def package_version(catalog, now=None):
    product = catalog["Product"]
    if (product["ProductId"] != PRODUCT_ID or product["ProductKind"] != "Application"
            or product["Properties"]["PackageIdentityName"] != IDENTITY):
        raise ValueError("The catalog response does not identify OTP Harbor's Store product")
    now = now or datetime.now(timezone.utc)
    versions = []
    for entry in product["DisplaySkuAvailabilities"]:
        sku = entry["Sku"]
        if sku["SkuType"] != "full" or sku["Properties"].get("IsTrial") is not False:
            continue
        available = any(
            "Purchase" in availability["Actions"] and "Fulfill" in availability["Actions"]
            and datetime.fromisoformat(availability["Conditions"]["StartDate"].replace("Z", "+00:00")) <= now
            < datetime.fromisoformat(availability["Conditions"]["EndDate"].replace("Z", "+00:00"))
            for availability in entry["Availabilities"]
        )
        if not available or sku["Properties"].get("IsPreOrder"):
            continue
        for package in sku["Properties"]["Packages"]:
            match = PACKAGE.fullmatch(package["PackageFullName"])
            if (not match or package["PackageFormat"] != "Msix"
                    or package.get("ResourceId", "") != ""
                    or package["FulfillmentData"]["ProductId"] != PRODUCT_ID):
                continue
            version = tuple(int(part) for part in match[1].split("."))
            if (any(part > 65535 for part in version) or version[0] == 0
                    or version[2:] != (65535, 0)):
                continue
            packed = sum(part << shift for part, shift in zip(version, (48, 32, 16, 0)))
            if int(package["Version"]) != packed:
                raise ValueError("The catalog package name and numeric version disagree")
            versions.append(version)
    if not versions:
        raise ValueError("No publicly available stable OTP Harbor x64 MSIX was found")
    return ".".join(str(part) for part in max(versions))


def fetch_catalog():
    request = urllib.request.Request(CATALOG_URL, headers={
        "Accept": "application/json", "User-Agent": "OTP-Harbor-Store-Version-Sync"
    })
    with urllib.request.urlopen(request, timeout=20) as response:
        payload = response.read(2 * 1024 * 1024 + 1)
    if len(payload) > 2 * 1024 * 1024:
        raise ValueError("The Store catalog response exceeds the size limit")
    return json.loads(payload)


def sync(catalog, repository_root=ROOT, check=False):
    version = package_version(catalog)
    current = json.loads((repository_root / "packaging/windows-store/public-version.json").read_text(encoding="utf-8-sig"))
    if tuple(map(int, version.split("."))) < tuple(map(int, current["packageVersion"].split("."))):
        raise ValueError("The Store catalog is older than the last confirmed public version")
    command = ["pwsh", "-NoProfile", "-File", str(repository_root / "scripts/release/Set-PublicMicrosoftStoreVersion.ps1"),
               "-RepositoryRoot", str(repository_root), "-PackageVersion", version]
    if check:
        command.append("-Check")
    subprocess.run(command, check=True)
    return version


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", type=Path, default=ROOT)
    parser.add_argument("--catalog-json", type=Path, help="Use a saved public catalog response for deterministic verification")
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--keep-current-on-error", action="store_true")
    args = parser.parse_args()
    try:
        catalog = json.loads(args.catalog_json.read_text(encoding="utf-8-sig")) if args.catalog_json else fetch_catalog()
        version = sync(catalog, args.repository_root, args.check)
        print(f"Verified public Microsoft Store package {version} for {PRODUCT_ID}.")
    except (KeyError, TypeError, ValueError, OSError) as error:
        if not args.keep_current_on_error:
            parser.exit(1, f"Public Microsoft Store version refresh failed: {error}\n")
        print(f"::warning::Could not refresh the public Microsoft Store version; retaining confirmed metadata: {error}")


if __name__ == "__main__":
    main()
