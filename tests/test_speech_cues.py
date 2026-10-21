import sys
from pathlib import Path
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"HN-IPTV/TranslationRuntime"))
from speech_cues import SpeechCueCommitter,split_cues
def words(values):
    return [dict(text=t,start=i*.4,end=(i+1)*.4,probability=.95) for i,t in enumerate(values)]
class CueTests(unittest.TestCase):
    def test_first_guesses_are_hidden_then_stable_sentence_commits_once(self):
        c=SpeechCueCommitter();w=words(["Hello","world."])
        self.assertEqual(c.update(w,2,0,True),[])
        self.assertEqual(c.update(w,3,0,True),["Hello world."])
        self.assertEqual(c.update(w,4,0,True),[])
    def test_revised_word_is_not_translated_until_confirmed(self):
        c=SpeechCueCommitter()
        c.update(words(["Ten","crystals."]),2,0,True)
        self.assertEqual(c.update(words(["Ten","storms."]),3,0,True),[])
        self.assertEqual(c.update(words(["Ten","storms."]),4,0,True),["Ten storms."])
    def test_incomplete_phrase_waits_for_boundary(self):
        c=SpeechCueCommitter();w=words(["The","storm","formed"])
        c.update(w,2,0)
        self.assertEqual(c.update(w,3,0),[])
        self.assertEqual(c.update(w,4,0,True),["The storm formed"])
    def test_low_confidence_cannot_be_finalized_by_silence(self):
        c=SpeechCueCommitter();w=words(["Uncertain."]);w[0]["probability"]=.2
        c.update(w,2,0,True)
        self.assertEqual(c.update(w,3,0,True),[])
    def test_confirmed_clause_can_send_before_twelve_words(self):
        c=SpeechCueCommitter();w=words(["The","storm","arrived","today,","and","continues"])
        self.assertEqual(c.update(w,4,0),[])
        self.assertEqual(c.update(w,5,0),["The storm arrived today,"])
    def test_cues_are_short_and_have_finite_reading_times(self):
        cues=split_cues(" ".join(["translation"]*40))
        self.assertGreater(len(cues),1)
        self.assertTrue(all(len(c["text"])<=76 and 1.8<=c["duration_seconds"]<=6 for c in cues))
if __name__=="__main__":unittest.main()

