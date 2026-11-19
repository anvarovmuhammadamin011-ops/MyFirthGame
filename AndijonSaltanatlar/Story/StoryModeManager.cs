using System;
using System.Collections.Generic;
using System.IO;
using AndijonSaltanatlar.Quests;
using AndijonSaltanatlar.Narrative;
using AndijonSaltanatlar.Campaigns;
using AndijonSaltanatlar.Achievements;
using AndijonSaltanatlar.Content;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Buildings;

namespace AndijonSaltanatlar.Story
{
    /// <summary>
    /// Story Mode integratsiya fasadi — PROMPT 13. Event bus, QuestManager, AchievementManager,
    /// DialogueManager va CampaignManager'ni yagona tizimga bog'laydi va mavjud o'yin (GameStateManager)
    /// bilan ulaydi: mukofotni haqiqiy iqtisodiyotga qo'llaydi, hodisalarni e'lon qiladi, Vazir tabrigini chiqaradi.
    /// Markaziy event bus o'yin kodida yo'q edi — shuning uchun hodisalar aniq integratsiya nuqtalaridan
    /// (bino, aholi, yer) shu fasad orqali e'lon qilinadi (PROMPT 13, 7 & 21-bo'lim).
    /// </summary>
    public class StoryModeManager
    {
        public GameEventBus Bus { get; private set; }
        public QuestManager Quests { get; private set; }
        public AchievementManager Achievements { get; private set; }
        public DialogueManager Dialogue { get; private set; }
        public CampaignManager Campaign { get; private set; }
        public CampaignState State { get; private set; }

        public bool StoryModeActive { get; private set; }
        public bool CampaignCompleted { get; private set; }
        /// <summary>Oxirgi bajarilgan vazifa (Vazir tabrigi / mukofot tugmasi uchun).</summary>
        public QuestDefinition JustCompletedQuest { get; private set; }

        private GameStateManager _game;
        private readonly HashSet<int> _constructedEmitted = new();

        public static string DefaultContentRoot => Path.Combine(AppContext.BaseDirectory, "Content");

        public void Initialize(GameStateManager game, CampaignState state, bool storyMode, string contentRoot = null)
        {
            _game = game;
            State = state ?? new CampaignState();
            StoryModeActive = storyMode || State.StoryModeActive;

            Bus = new GameEventBus();
            var bundle = StoryContentLoader.Load(contentRoot ?? DefaultContentRoot);

            Quests = new QuestManager();
            Quests.LoadCatalog(bundle.Quests);
            Quests.RewardApplier = ApplyReward;
            Quests.OnQuestCompleted = q => { JustCompletedQuest = q; VazirCongratulate(q); };
            Quests.OnQuestAvailable = VazirExplain;

            Achievements = new AchievementManager();
            Achievements.LoadCatalog(bundle.Achievements);
            Achievements.RewardApplier = ApplyReward;

            Dialogue = new DialogueManager();
            Dialogue.LoadCatalog(bundle.Dialogues);
            Dialogue.Bind(State);

            Campaign = new CampaignManager();
            Campaign.OnShowDialogue = id => Dialogue.Open(id);
            Campaign.OnChapterStarted = ch => { };
            Campaign.OnCampaignCompleted = c => CampaignCompleted = true;

            Quests.Initialize(State, Bus);
            Achievements.Initialize(State, Bus);

            if (StoryModeActive)
            {
                State.StoryModeActive = true;
                Campaign.Begin(bundle.Campaign, State, Quests, Dialogue);
            }
            else
            {
                // Erkin Saltanat: hikoya vazifalari majburiy emas, lekin yutuqlar ishlaydi.
                Quests.RefreshAvailability();
            }
        }

        // ---- Mukofotni haqiqiy o'yin holatiga qo'llash ----
        private void ApplyReward(QuestReward r)
        {
            if (r == null || _game == null) return;
            switch (r.Kind)
            {
                case RewardKind.Coin: _game.Coins += r.Amount; break;
                case RewardKind.Food: _game.Food += r.Amount; break;
                case RewardKind.Science: if (_game.Civ?.Player != null) _game.Civ.Player.Science += r.Amount; break;
                case RewardKind.Culture: if (_game.Civ?.Player != null) _game.Civ.Player.Culture += r.Amount; break;
                case RewardKind.Experience: State.Experience += r.Amount; break;
                case RewardKind.Title: if (!string.IsNullOrEmpty(r.RefKey)) State.Titles.Add(r.RefKey); break;
                case RewardKind.Decoration: if (!string.IsNullOrEmpty(r.RefKey)) State.Decorations.Add(r.RefKey); break;
                // Wood/Stone: o'yinda resurs tizimi yo'q — kontentda ishlatilmaydi.
            }
        }

        // ---- Vazir integratsiyasi: yangi vazifa yoki muhim voqeada Vazir ekranda paydo bo'lib aytadi ----
        private void VazirExplain(QuestDefinition q)
        {
            if (q == null) return;
            // Yangi vazifa ochildi — Vazir tushuntiradi (Story va Erkin rejimda ham).
            string body = string.IsNullOrWhiteSpace(q.DescriptionUz)
                ? "Hazratim, yangi vazifa paydo bo'ldi: " + q.TitleUz
                : q.DescriptionUz;
            _game?.ShowVizier("Yangi vazifa: " + q.TitleUz, body);
        }

        private void VazirCongratulate(QuestDefinition q)
        {
            if (q == null) return;
            _game?.ShowVizier("Vazifa bajarildi", "Tabriklayman, hazratim! «" + q.TitleUz + "» vazifasi muvaffaqiyatli ado etildi.");
        }

        // ---- Hodisa e'lon qilish nuqtalari (GameStateManager chaqiradi) ----

        /// <summary>Qurilish yakunlanganda (faqat o'yinchi qurgan binolar, bir marta).</summary>
        public void NotifyBuildingConstructed(Building b)
        {
            if (Bus == null || b == null || !b.BuiltByPlayer) return;
            if (_constructedEmitted.Contains(b.Id)) return;
            _constructedEmitted.Add(b.Id);
            Bus.Publish(new GameEvent(GameEventType.BuildingConstructed)
                .With("kind", b.Type.ToString())
                .With("id", b.Id.ToString()));
            Quests.AutoActivateAvailable();
        }

        public void NotifyBuildingUpgraded(Building b)
        {
            if (Bus == null || b == null) return;
            Bus.Publish(new GameEvent(GameEventType.BuildingUpgraded)
                .With("kind", b.Type.ToString())
                .With("level", b.Level.ToString()));
        }

        public void NotifyLandUnlocked()
        {
            if (Bus == null) return;
            Bus.Publish(new GameEvent(GameEventType.LandUnlocked));
            Quests.AutoActivateAvailable();
        }

        public void NotifyTechnologyUnlocked(string techId)
        {
            if (Bus == null) return;
            Bus.Publish(new GameEvent(GameEventType.TechnologyUnlocked).With("key", techId ?? ""));
        }

        public void NotifyTutorialStep(string stepKey)
        {
            if (Bus == null) return;
            Bus.Publish(new GameEvent(GameEventType.TutorialStepCompleted).With("key", stepKey ?? ""));
        }

        /// <summary>Kunlik tick: aholi/baxt/xazina holatini e'lon qiladi va kampaniya oqimini yuritadi.</summary>
        public void OnDayTick()
        {
            if (Bus == null || _game == null) return;
            int happiness = 0;
            try { if (_game.Pop != null) happiness = (int)_game.Pop.AvgHappiness; } catch { }
            Bus.Publish(new GameEvent(GameEventType.PopulationChanged)
                .With("population", _game.Population.ToString())
                .With("happiness", happiness.ToString())
                .With("coinTotal", _game.Coins.ToString()));
            Campaign?.Update();
        }

        /// <summary>UI "mukofotni ol" tugmasi uchun.</summary>
        public bool ClaimReward(string questId) => Quests != null && Quests.ClaimReward(questId);
    }
}
