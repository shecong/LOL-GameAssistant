namespace LOL_GameAssistant.LoLApi
{
    public class Select_Api
    {
        public static async Task<Stream> GetImg(String actionId)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($@"/lol-champ-select/v1/session/actions/{actionId}");
            if (responseStream == null)
            {
                return Stream.Null;
            }
            return responseStream;
        }
    }
}