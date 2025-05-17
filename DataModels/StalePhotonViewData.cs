// File: L.A.T.E/DataModels/StalePhotonViewData.cs
namespace LATE.DataModels
{
    public readonly struct StalePhotonViewData
    {
        public int InstantiationId { get; }
        public int ViewID { get; } // Useful for logging and RemoveBufferedRPCs

        public StalePhotonViewData(int instantiationId, int viewId)
        {
            InstantiationId = instantiationId;
            ViewID = viewId;
        }
    }
}