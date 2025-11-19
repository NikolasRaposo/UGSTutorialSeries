using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using UnityEngine;

namespace PlayerLogin 
{
    /// <summary>
    /// Gerencia o fluxo de autenticacao do jogador (Anonimo e Unity Player Account).
    /// </summary>
    public class LoginManager : MonoBehaviour
    {
        private async void Awake()
        {
            // Verifica se os servicos da Unity ja foram inicializados
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                Debug.Log("Inicializando Servicos...");
                await UnityServices.InitializeAsync();
            }
            
            // Inscreve o metodo de login/vinculo no evento de assinatura
            PlayerAccountService.Instance.SignedIn += SignInOrLinkWithUnity;
        }

        async void Start()
        {
            // Verifica se existe um token de sessao salvo para login automatico
            if (!AuthenticationService.Instance.SessionTokenExists)
            {
                Debug.Log("Token de sessao nao encontrado");
                return;
            }
            Debug.Log("Solicitando login do jogador...");
            await SignInAnonymouslyAsync();
        }

        /// <summary>
        /// Metodo publico para iniciar o login anonimo (ex: via botao).
        /// </summary>
        public async void StartAnonymousSignIn()
        {
            await SignInAnonymouslyAsync();
        }

        /// <summary>
        /// Logica interna para realizar o login anonimo assincrono.
        /// </summary>
        private async Task SignInAnonymouslyAsync()
        {
            try
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log("Login anonimo realizado com sucesso!");
    
                // Mostra como obter o PlayerID
                Debug.Log($"ID do Jogador: {AuthenticationService.Instance.PlayerId}");
    
            }
            catch (AuthenticationException ex)
            {
                // Compare o codigo de erro com AuthenticationErrorCodes
                // Notifique o jogador com a mensagem de erro adequada
                Debug.LogException(ex);
            }
            catch (RequestFailedException ex)
            {
                // Compare o codigo de erro com CommonErrorCodes
                // Notifique o jogador com a mensagem de erro adequada
                Debug.LogException(ex);
            }
        }
        
        /// <summary>
        /// Inicia o fluxo de login com a Unity Player Account.
        /// </summary>
        public async void StartUnitySignInAsync()
        {
            // Se ja estiver logado no servico de contas, tenta vincular ou logar no AuthenticationService
            if (PlayerAccountService.Instance.IsSignedIn)
            {
                SignInOrLinkWithUnity();
                return;
            }

            try
            {
                // Abre o portal de login da Unity
                await PlayerAccountService.Instance.StartSignInAsync();
            }
            catch (RequestFailedException ex)
            {
                Debug.LogException(ex);
            }
        }
        
        /// <summary>
        /// Decide se deve fazer login ou vincular a conta atual com a conta Unity.
        /// </summary>
        async void SignInOrLinkWithUnity()
        {
            try
            {
                // 1. O jogador ainda nao esta autenticado, entao faz o login com a Unity
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    Debug.Log("Entrando com conta Unity Player Account...");
                    await AuthenticationService.Instance.SignInWithUnityAsync(PlayerAccountService.Instance.AccessToken);
                    Debug.Log("Entrou com sucesso na conta Unity Player Account");
                    return;
                }

                // 2. O jogador esta autenticado (ex: anonimo), mas ainda nao tem um Unity ID, entao vamos vincular
                if (!HasUnityID())
                {
                    Debug.Log("Vinculando conta anonima a Unity...");
                    await LinkWithUnityAsync(PlayerAccountService.Instance.AccessToken);
                    Debug.Log("Conta anonima vinculada com sucesso!");
                    return;
                }

                // 3. O jogador ja tem autenticacao e um Unity ID
                Debug.Log("O jogador ja esta logado em sua conta Unity Player Account");
            }
            catch (RequestFailedException ex)
            {
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Verifica se as informacoes do jogador contem um Unity ID valido.
        /// </summary>
        private bool HasUnityID()
        {
            return AuthenticationService.Instance.PlayerInfo.GetUnityId() != null;
        }
        
        /// <summary>
        /// Tenta vincular a sessao atual com a conta Unity fornecida.
        /// </summary>
        private async Task LinkWithUnityAsync(string accessToken)
        {
            try
            {
                await AuthenticationService.Instance.LinkWithUnityAsync(accessToken);
                Debug.Log("Vinculo realizado com sucesso.");
            }
            catch (AuthenticationException ex) when (ex.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                // Avisa o jogador com uma mensagem de erro especifica.
                Debug.LogError("Este usuario ja esta vinculado com outra conta. Faca login em vez disso.");
            }
            catch (AuthenticationException ex)
            {
                // Compare o codigo de erro com AuthenticationErrorCodes
                // Notifique o jogador com a mensagem de erro adequada
                Debug.LogException(ex);
            }
            catch (RequestFailedException ex)
            {
                // Compare o codigo de erro com CommonErrorCodes
                // Notifique o jogador com a mensagem de erro adequada
                Debug.LogException(ex);
            }
        }
    }
}