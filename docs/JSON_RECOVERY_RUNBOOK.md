# JSON backup and restore rehearsal

These instructions apply to the single-replica JSON deployment. The budget track keeps that provider; PostgreSQL cutover is deferred. Phase 1B hardens recovery without adding a new data schema. Verify the deployed binary: older production images still have the original unsafe fallback until this change is deployed.

## Capture

1. Record the deployed commit/image and configuration variable names. Store secrets in the existing secret manager, never in a report or this repository.
2. Pause writes and the reminder worker. The new API host supports `RIFFROSTER_READ_ONLY=true`; on an older deployed image without that feature, use deployment controls to stop the one replica if necessary. Wait for the former writable instance to stop. An in-app daily snapshot may predate the most recent writes.
3. Copy the entire `store.json` and persistent data-protection key directory into a new protected, immutable backup location. Preserve the original file bytes. Keep VAPID keys separately in the secret manager. Never commit backup contents.
4. Run the read-only validator on that copy. It prints schema version, SHA-256 and counts only:

```text
python tools/verify-json-backup.py /protected/snapshot/store.json
```

Versions 5, 6 and 7 are characterized. Older/future versions, duplicate IDs, orphan selections/memberships/devices/deliveries, missing collections and nonpositive/offset-free performance times fail closed. This check validates structural integrity, not whether a user intended every row. Keep an independent hash and collection counts with the deployment record.

## Rehearse without touching live data

Create an empty private rehearsal directory first, then:

```text
python tools/verify-json-backup.py /protected/snapshot/store.json --copy-to /isolated/rehearsal/store.json
```

The destination must be a new file and its parent must already exist. Overwrites are refused, including copying onto the source. Bytes, SHA-256 and integrity are checked again after copying. The tool never boots the app or runs migrations. A failed write can leave a partial destination; choose a new path for a retry and retain the source.

Start the matching app image against a second disposable copy of the rehearsal store. Set `RIFFROSTER_DATA_PATH`, `RIFFROSTER_BACKUP_DIR`, and `RIFFROSTER_KEYS_PATH` to isolated paths. Explicitly clear all three VAPID settings to prevent notification delivery. Do not expose the rehearsal service publicly. Existing production records contain personal data and push credentials even when notifications are disabled.

Check health, account sign-in using an authorized test account, events/days/performances, group membership, picks and display-name privacy. Compare stable IDs and all collection counts. Record any expected schema change; the v5/v6 loader will upgrade the disposable copy. Automated tests exercise this procedure with synthetic data and verify loading of every current collection, credentials claiming, delivery history and stable IDs.

## Restore live service

Restoring production is a separate destructive operation requiring an explicit target and approval. Stop writes first, preserve the current failed store as evidence, select the validated snapshot and matching deployment, restore to the configured volume and verify before reopening traffic. Restore the corresponding key material if session continuity is required. Account for picks made since the snapshot: they will not exist in the restored file. Never copy a snapshot over a running app's store.

## Loader behavior and remaining limits

The hardened loader validates v5–7 input and stops on malformed, incomplete, unsupported or orphaned data. It does not reseed an existing invalid store. Supported v5/v6 upgrades first retain a byte-identical `before-vN-upgrade-*.json` copy, then normalize to v7. Pre-v5 seed-changing migrations are no longer automatic. A missing file still bootstraps a new installation, so confirm the configured volume and source path before starting a restored deployment; never delete the only source as a recovery step.

Saves use unique temporary files in the same directory, flush file contents before replacement, and update committed memory only after replacement succeeds. Failed or unsaved sessions do not leak mutations into subsequent reads. Tests cover replacement failure, retry and restart; they do not certify every filesystem or power-loss scenario. JSON remains limited to one process/replica.

Daily snapshots retain 14 files and validate input/existing daily snapshots before reporting success. Pre-upgrade copies are not pruned by that daily retention. These files share the same volume, so keep separately verified off-volume copies. Automated off-volume transfer is still deferred; record the operator and verification date. A future database migration must dry-run full ID mapping and retain the immutable JSON backup through two festival cycles.
