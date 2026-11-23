# Derived UI frames

`trainer_*_blink_aligned.png` are built from the generated base frame `trainer_*_v1.png` and the generated edit
`trainer_*_blink_v1.png` (OpenAI Images edit of the base, see the pipeline manifest): `tools/align_blink_frame.py`
aligns the edit to the base (scale + shift search on the head) and pastes only the eye band with a soft edge, so
switching base ↔ blink in the lobby idle animation does not jitter. No external images are involved.
