#!/usr/bin/env bash

sudo apt install gnupg ca-certificates -y
sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
echo "deb https://download.mono-project.com/repo/ubuntu stable-bionic main" | sudo tee /etc/apt/sources.list.d/mono-official-stable.list
sudo apt update -y
sudo apt install mono-complete -y
cd /home/user
wget https://github.com/TheRetroMike/HiveOSProfitSwitcher/releases/latest/download/HiveProfitSwitcher.zip
unzip HiveProfitSwitcher.zip -d /usr/profit-switcher
printf "\n0/15 * * * * mono /usr/profit-switcher/HiveProfitSwitcher.exe\n" >> /hive/etc/crontab.root
printf "..................\nHive Profit Switcher has been installed, but must be configured for you instance. \nPlease run the following command to setup the config file, save it, and then make sure to reboot the rig\nnano /usr/profit-switcher/HiveProfitSwitcher.exe.config\n..................\n"
