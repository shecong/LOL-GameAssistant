namespace LOL_GameAssistant.LoLApi
{
    public class Select_Api
    {
        public static Stream GetImg(String actionId)
        {
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($@"/lol-champ-select/v1/session/actions/{actionId}");
            return new MemoryStream(Convert.FromBase64String(result.Result));
        }
    }
}