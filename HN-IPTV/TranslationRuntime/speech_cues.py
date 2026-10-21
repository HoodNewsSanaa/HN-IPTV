"""Commit stable recognition phrases, never provisional rolling paragraphs."""
import re
import unicodedata

def key(text):
    return "".join(c for c in unicodedata.normalize("NFKC", text).casefold() if c.isalnum())

class SpeechCueCommitter:
    def __init__(self):
        self.previous = []
        self.committed_until = -1.0

    def pending(self):
        return any(w["end"] > self.committed_until + .08 for w in self.previous)

    def update(self, words, audio_end, window_start, final=False):
        # Timestamps share the capture clock, even when queued windows were dropped.
        words = [dict(w) for w in words if key(w["text"]) and
            w["end"] > max(self.committed_until+.08, window_start) and w["start"] >= self.committed_until-.15]
        ready = []
        for word in words:
            if word["end"] > audio_end-.6:
                break
            matched = any(key(old["text"]) == key(word["text"]) and
                abs(old["end"]-word["end"]) < .8 for old in self.previous)
            # A completed phrase still needs a second recognition pass. Initial guesses stay hidden.
            if not matched or word.get("probability", 0) < .15:
                break
            ready.append(word)
        self.previous = words
        phrases = []
        group = []
        for index, word in enumerate(ready):
            if group and word["start"]-group[-1]["end"] > .6:
                phrases.append(group); group = []
            group.append(word)
            sentence_end = bool(re.search(r"[.!?؟…][\"'»”)]*$", word["text"]))
            length_limit = len(group) >= 12 or sum(len(w["text"])+1 for w in group) >= 90
            if sentence_end or length_limit:
                phrases.append(group); group = []
        if group and final and len(ready) == len(words):
            phrases.append(group)
        # One uncertain name/number must not starve an otherwise confident phrase.
        phrases = [p for p in phrases if sum(w.get("probability",0) for w in p)/len(p) >= .65]
        for phrase in phrases:
            self.committed_until = max(self.committed_until, phrase[-1]["end"])
        return [" ".join(w["text"].strip() for w in phrase).strip() for phrase in phrases]

def split_cues(text):
    """Keep the display compact: roughly two lines, each cue expires independently."""
    words = text.split()
    cues = []
    current = []
    for word in words:
        if current and (len(" ".join(current+[word])) > 76 or len(current) >= 12):
            value = " ".join(current)
            cues.append({"text":value, "duration_seconds":min(6,max(1.8,len(value)/15))})
            current = []
        current.append(word)
    if current:
        value = " ".join(current)
        cues.append({"text":value, "duration_seconds":min(6,max(1.8,len(value)/15))})
    if len(cues) >= 2 and len(cues[-1]["text"]) < 18:
        before, tail = cues[-2]["text"].split(), cues[-1]["text"].split()
        while len(before) > 2 and len(" ".join(tail)) < 20:
            tail.insert(0,before.pop())
        for cue, words in ((cues[-2],before),(cues[-1],tail)):
            cue["text"] = " ".join(words)
            cue["duration_seconds"] = min(6,max(1.8,len(cue["text"])/15))
    return cues

