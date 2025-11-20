using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using UnityEngine;
using Facebook.Unity;
namespace PlayerLogin
{
    /// <summary>
    /// Gerencia o fluxo de autenticacao do jogador (Anonimo e Unity Player Account).
    /// </summary>
    public class LoginManager : MonoBehaviour
    {
        public Action PlayerSignedIn;
        private void Awake()
        {
            InitializeFacebook();
            if (UnityServices.State == ServicesInitializationState.Initialized)
            {
                UnitySignInSubscription();
            }
            else
            {
                UnityServices.Initialized += UnitySignInSubscription;
            }
        }

        private void UnitySignInSubscription()
        {
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

                PlayerSignedIn.Invoke();
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
        private void InitializeFacebook()
        {
            if (!FB.IsInitialized)
            {
                // Inicializa o Facebook SDK
                FB.Init(InitCallback, OnHideUnity);
            }
            else
            {
                // Ja inicializado, envia um evento de aplicativo ativado para o aplicativo
                FB.ActivateApp();
            }
        }
        void InitCallback()
        {
            if (FB.IsInitialized)
            {
                Debug.Log($"[DIAGNOSTICO] App ID carregado pelo SDK: '{FB.AppId}'");
                Debug.Log($"[DIAGNOSTICO] Client Token carregado: '{FB.ClientToken}'");

                FB.ActivateApp();
            }
            else
            {
                Debug.Log("Falha ao inicializar o Facebook SDK");
            }
        }
        void OnHideUnity(bool isGameShown)
        {
            if (!isGameShown)
            {
                // Pausa o jogo - vamos precisar esconder
                Time.timeScale = 0;
            }
            else
            {
                // Retorna ao jogo - estamos focados novamente
                Time.timeScale = 1;
            }
        }
        public void StartFacebookSignIn()
        {
            var perms = new List<string>() { "public_profile", "email" };
            FB.LogInWithReadPermissions(perms, async result => {
                if (FB.IsLoggedIn)
                {
                    // A classe AccessToken vai receber os detalhes da sessao
                    var facebookAccessToken = Facebook.Unity.AccessToken.CurrentAccessToken.TokenString;

                    if (!AuthenticationService.Instance.IsSignedIn)
                    {
                        await SignInWithFacebookAsync(facebookAccessToken);
                    }
                    else
                    {
                        await LinkWithFacebookAsync(facebookAccessToken);
                    }
                }
                else
                {
                    Debug.LogError("Login nao completado.");
    
                    // Verifica se houve cancelamento pelo usuario
                    if (result.Cancelled)
                    {
                        Debug.LogWarning("O usuario fechou a janela de login ou cancelou a permissao.");
                    }
    
                    // Verifica se houve erro tecnico (App ID errado, sem internet, etc)
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        Debug.LogError($"Erro retornado pelo Facebook: {result.Error}");
                    }
    
                    // Mostra a resposta crua para analise profunda
                    Debug.Log($"Resposta Raw: {result.RawResult}");
                }
            });

        }
        private async Task SignInWithFacebookAsync(string accessToken)
        {
            try
            {
                await AuthenticationService.Instance.SignInWithFacebookAsync(accessToken);
                Debug.Log("Entrou com o Facebook!");
            }
            catch (RequestFailedException ex)
            {
                Debug.LogException(ex);
            }
        }
        private async Task LinkWithFacebookAsync(string accessToken)
        {
            try
            {
                await AuthenticationService.Instance.LinkWithFacebookAsync(accessToken);
                Debug.Log("Conectado com o Facebook!");
            }
            catch (AuthenticationException ex) when (ex.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                Debug.LogException(ex);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
        private void OnDestroy()
        {
            PlayerAccountService.Instance.SignedIn -= SignInOrLinkWithUnity;
            UnityServices.Initialized -= UnitySignInSubscription;
        }
    }
}
