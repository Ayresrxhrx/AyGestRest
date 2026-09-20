using AyGestRest.Models;

namespace AyGestRest.Models
{
    public static class AppSession
    {
        public static User? CurrentUser { get; private set; }
        public static RestaurantConfig RestaurantConfig { get; internal set; }
      
        public static void SetUser(User user)
        {
            if (user == null || user.Id <= 0)
                throw new Exception("Utilizador inválido");

            CurrentUser = user;
        }

        public static void Clear()
        {
            CurrentUser = null;
        }
    }
}
