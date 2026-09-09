"""Read-only JSON integrity check and exclusive-copy restore drill (Python stdlib)."""
import argparse
import hashlib
import json
from datetime import datetime
from pathlib import Path


def validate(raw):
    state = json.loads(raw)
    if not isinstance(state, dict) or state.get("dataVersion") not in (5, 6, 7):
        raise ValueError("Only characterized store versions 5, 6 and 7 are supported")
    fields = ("events", "users", "selections", "pushDevices", "reminderDeliveries", "groups", "groupMemberships")
    for field in fields:
        if field not in state and state["dataVersion"] == 5 and field in ("groups", "groupMemberships"):
            state[field] = []
        if not isinstance(state.get(field), list):
            raise ValueError(f"Missing or invalid collection: {field}")

    def unique(items, key, label):
        values = [key(item) for item in items]
        if any(not value for value in values) or len(set(values)) != len(values):
            raise ValueError(f"Missing or duplicate keys: {label}")
        return set(values)

    events = unique(state["events"], lambda item: item["id"], "events")
    users = unique(state["users"], lambda item: item["username"].casefold(), "users")
    days = [day for event in state["events"] for day in event["days"]]
    unique(days, lambda item: item["id"], "days")
    performances = [act for day in days for act in day["performances"]]
    acts = unique(performances, lambda item: item["id"], "performances")
    groups = unique(state["groups"], lambda item: item["id"], "groups")
    devices = unique(state["pushDevices"], lambda item: item["id"], "push devices")
    memberships = unique(state["groupMemberships"], lambda item: (item["groupId"], item["username"].casefold()), "memberships")
    unique(state["selections"], lambda item: (item["username"].casefold(), item["performanceId"]), "selections")
    unique(state["reminderDeliveries"], lambda item: (item["pushDeviceId"], item["performanceId"], item["performanceStartsAt"]), "deliveries")
    for act in performances:
        start, end = (datetime.fromisoformat(act[field].replace("Z", "+00:00")) for field in ("startsAt", "endsAt"))
        if start.tzinfo is None or end.tzinfo is None or end <= start:
            raise ValueError("Performance has invalid instants or duration")
    for user in state["users"]:
        if user.get("lastSelectedEventId") and user["lastSelectedEventId"] not in events:
            raise ValueError("Orphan last selected event")
    for selection in state["selections"]:
        if selection["username"].casefold() not in users or selection["performanceId"] not in acts:
            raise ValueError("Orphan selection")
    for member in state["groupMemberships"]:
        if member["groupId"] not in groups or member["username"].casefold() not in users:
            raise ValueError("Orphan membership")
    for group in state["groups"]:
        if (group["id"], group["createdBy"].casefold()) not in memberships:
            raise ValueError("Group owner is not a member")
    for device in state["pushDevices"]:
        if device["username"].casefold() not in users:
            raise ValueError("Orphan push device")
    for delivery in state["reminderDeliveries"]:
        if delivery["pushDeviceId"] not in devices or delivery["performanceId"] not in acts:
            raise ValueError("Orphan reminder delivery")
    return {"dataVersion": state["dataVersion"], "sha256": hashlib.sha256(raw).hexdigest(),
            "counts": {**{field: len(state[field]) for field in fields}, "days": len(days), "performances": len(performances)}}


def verify(source, copy_to=None):
    raw = Path(source).read_bytes()
    report = validate(raw)
    if copy_to is not None:
        # Exclusive creation prohibits overwriting any store or the source itself.
        # Parent must already exist, making the drill destination explicit.
        with Path(copy_to).open("xb") as target:
            target.write(raw)
        restored = Path(copy_to).read_bytes()
        if restored != raw or validate(restored) != report:
            raise ValueError("Restore copy failed integrity verification")
        report["restoreCopyVerified"] = True
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--copy-to", type=Path, help="New file in an existing isolated directory; never a live store")
    args = parser.parse_args()
    try:
        print(json.dumps(verify(args.source, args.copy_to), indent=2))
    except (OSError, ValueError, KeyError, TypeError):
        # Do not print parse exceptions, raw data, usernames or paths from production input.
        parser.exit(1, "Verification failed: invalid/unsupported store, inconsistent references, or unavailable/new-file-only destination. Source was not modified.\n")
