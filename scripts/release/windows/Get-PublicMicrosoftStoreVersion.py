"""Read the publicly available package version using the existing catalog verifier."""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location('store_catalog', Path(__file__).with_name('Sync-PublicMicrosoftStoreVersion.py'))
catalog = importlib.util.module_from_spec(spec)
spec.loader.exec_module(catalog)

if __name__ == '__main__':
    print(catalog.package_version(catalog.fetch_catalog()))
