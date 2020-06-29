Vagrant.configure("2") do |config|
  config.vm.box = "debian/stretch64"
  config.vm.network "forwarded_port", guest: 80, host: 8080

  config.vm.provision "shell", inline: <<-SHELL

    # Docker
    sudo apt install -y apt-transport-https ca-certificates curl gnupg2 software-properties-common
    curl -fsSL https://download.docker.com/linux/debian/gpg | sudo apt-key add -
    sudo add-apt-repository "deb [arch=amd64] https://download.docker.com/linux/debian $(lsb_release -cs) stable"
    sudo apt update
    sudo apt install -y docker-ce

    # Minikube
    apt install -y curl
    curl -Lo minikube https://storage.googleapis.com/minikube/releases/latest/minikube-linux-amd64
    chmod +x minikube
    mkdir -p /usr/local/bin/
    install minikube /usr/local/bin/
    sudo apt-get install conntrack -y
    sudo -E minikube start --driver=none

    # kubectl
    sudo apt-get update && sudo apt-get install -y apt-transport-https gnupg2
    curl -s https://packages.cloud.google.com/apt/doc/apt-key.gpg | sudo apt-key add -
    echo "deb https://apt.kubernetes.io/ kubernetes-xenial main" | sudo tee -a /etc/apt/sources.list.d/kubernetes.list
    sudo apt-get update
    sudo apt-get install -y kubectl

    # configure kubectl
    sudo mv /home/vagrant/.kube /home/vagrant/.minikube $HOME
    sudo chown -R $USER $HOME/.kube $HOME/.minikube

    # deploy
    cd /vagrant
    kubectl apply -f k8s-secret.sample.yaml
    kubectl apply -f k8s-deployment.yaml
    kubectl rollout status deployments/justaml -w

    #sudo kubectl port-forward $(sudo kubectl get po | tail -1 | awk '{print $1}') 8080:80
  SHELL
end
