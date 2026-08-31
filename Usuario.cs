namespace PelComSystem.Models
{
    public class Usuario
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public string Telefone { get; set; }
        public string Posto{ get; set; }
        public string SenhaHash { get; set; }
        public string Token { get; set; }
    }
}