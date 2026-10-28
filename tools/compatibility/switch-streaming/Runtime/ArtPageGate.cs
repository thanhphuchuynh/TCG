using System.Collections.Generic;
using UnityEngine;

namespace TCG.SwitchStreaming
{
    // A page is published only after every active card has completed its bind.
    // Original page membership, sort order and card visibility stay with the game.
    public sealed class ArtPageGate : MonoBehaviour
    {
        readonly List<Component> cards = new List<Component>();
        CanvasGroup group;
        float alpha;
        bool waiting, interactable, blocksRaycasts;

        public static void Watch(Component page, IEnumerable<Component> owners)
        {
            if (!page || !UnityArtService.Available) return;
            var gate = page.GetComponent<ArtPageGate>();
            if (!gate) gate = page.gameObject.AddComponent<ArtPageGate>();
            gate.Begin(owners);
        }
        void Begin(IEnumerable<Component> owners)
        {
            foreach (var card in cards) CardArtSession.CancelPrime(card);
            cards.Clear();
            foreach (var card in owners)
            {
                if (!card || !VisibleWithinPage(card.transform)) continue;
                cards.Add(card); CardArtSession.SetVisible(card, true); CardArtSession.Prime(card);
            }
            if (!group) group = GetComponent<CanvasGroup>();
            if (!group) group = gameObject.AddComponent<CanvasGroup>();
            if (!waiting) { alpha = group.alpha; interactable = group.interactable; blocksRaycasts = group.blocksRaycasts; }
            waiting = true; group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
        }
        bool VisibleWithinPage(Transform card)
        {
            // Pages are often prepared before their own parent is activated.
            // Include those upcoming cards, but exclude explicitly empty slots.
            for (var node = card; node && node != transform; node = node.parent)
                if (!node.gameObject.activeSelf) return false;
            return card.IsChildOf(transform);
        }
        void LateUpdate()
        {
            if (!waiting) return;
            foreach (var card in cards)
                if (card && card.gameObject.activeInHierarchy && (!CardArtSession.Ready(card) || !CardArtSession.Displayed(card))) return;
            foreach (var card in cards) CardArtSession.CancelPrime(card);
            Restore();
        }
        void Restore()
        {
            if (waiting && group) { group.alpha = alpha; group.interactable = interactable; group.blocksRaycasts = blocksRaycasts; }
            waiting = false;
        }
        void OnDisable()
        {
            foreach (var card in cards) CardArtSession.CancelPrime(card);
            cards.Clear();
            Restore();
        }
    }
}
