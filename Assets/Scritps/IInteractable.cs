public interface IInteractable
{
    /// <summary>
    /// Wird vom Player aufgerufen, wenn Interaktion erkannt wird.
    /// </summary>
    /// <param name="actorName">Name des Spielers</param>
    /// <param name="action">z. B. "activate", "collect"</param>
    void OnInteract(string actorName, string action);
}