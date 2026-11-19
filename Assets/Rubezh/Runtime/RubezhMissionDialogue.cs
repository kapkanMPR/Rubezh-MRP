using System;
using System.Globalization;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        [Serializable]
        sealed class MissionDialogueData
        {
            public int mission;
            public float charactersPerSecond = 32;
            public MissionDialogueLine[] lines;
        }

        [Serializable]
        sealed class MissionDialogueLine
        {
            public string speaker;
            public string portrait;
            public string text;
            public bool mortarSalvo;
        }

        MissionDialogueData missionDialogue;
        Texture2D[] dialoguePortraits;
        int dialogueIndex,
            dialogueVisible;
        int[] dialogueCharacters;
        float dialogueElapsed;
        string dialogueText = "";
        bool MissionDialogueActive => missionDialogue != null;

        void BeginMissionDialogue()
        {
            ResetStoryMortars();
            missionDialogue = null;
            dialoguePortraits = null;
            // Only a new campaign battle, never resume, quick battle or endless mode.
            if (!campaignMode || battle == null || battle.CampaignLevel != 19)
                return;
            var asset = Resources.Load<TextAsset>("Story/mission19");
            if (asset == null)
                return;
            try
            {
                var data = JsonUtility.FromJson<MissionDialogueData>(asset.text);
                if (data == null || data.mission != 19 || data.lines == null || data.lines.Length == 0)
                    return;
                foreach (var line in data.lines)
                    if (line == null || string.IsNullOrWhiteSpace(line.text))
                        throw new ArgumentException("Пустая реплика сюжетного диалога");
                dialoguePortraits = new Texture2D[data.lines.Length];
                for (int i = 0; i < data.lines.Length; i++)
                    if (!string.IsNullOrEmpty(data.lines[i].portrait))
                        dialoguePortraits[i] = ImportedPortrait(data.lines[i].portrait);
                LoadStoryMortarAudio();
                missionDialogue = data;
                dialogueIndex = 0;
                paused = true;
                accumulator = 0;
                CancelTouchInput();
                StartDialogueLine();
            }
            catch (Exception ex)
            {
                missionDialogue = null;
                Debug.LogWarning("Не удалось открыть сюжетный диалог: " + ex.Message);
            }
            finally
            {
                Resources.UnloadAsset(asset);
            }
        }

        void StartDialogueLine()
        {
            dialogueCharacters = StringInfo.ParseCombiningCharacters(missionDialogue.lines[dialogueIndex].text);
            dialogueElapsed = 0;
            dialogueVisible = 0;
            dialogueText = "";
        }

        void UpdateMissionDialogue()
        {
            if (!MissionDialogueActive)
                return;
            paused = true;
            accumulator = 0;
            dialogueElapsed += Mathf.Min(Time.unscaledDeltaTime, .1f);
            int count = Mathf.Min(
                dialogueCharacters.Length,
                Mathf.FloorToInt(dialogueElapsed * Mathf.Clamp(missionDialogue.charactersPerSecond, 1, 120))
            );
            if (count > dialogueVisible)
                RevealDialogue(count);
            if (dialogueVisible == dialogueCharacters.Length && missionDialogue.lines[dialogueIndex].mortarSalvo)
                TickDialogueSalvo();
        }

        void RevealDialogue(int count)
        {
            dialogueVisible = count;
            string text = missionDialogue.lines[dialogueIndex].text;
            dialogueText = count >= dialogueCharacters.Length ? text : text.Substring(0, dialogueCharacters[count]);
        }

        void AdvanceDialogue()
        {
            if (dialogueVisible < dialogueCharacters.Length)
            {
                RevealDialogue(dialogueCharacters.Length);
                return;
            }
            if (missionDialogue.lines[dialogueIndex].mortarSalvo)
                return; // The three launches finish before combat starts.
            if (++dialogueIndex < missionDialogue.lines.Length)
            {
                StartDialogueLine();
                return;
            }
            FinishMissionDialogue();
        }

        void FinishMissionDialogue()
        {
            missionDialogue = null;
            dialoguePortraits = null;
            dialogueCharacters = null;
            dialogueText = "";
            accumulator = 0;
            paused = false;
            CancelTouchInput(); // The finishing tap must not select or command a unit.
        }
    }
}
