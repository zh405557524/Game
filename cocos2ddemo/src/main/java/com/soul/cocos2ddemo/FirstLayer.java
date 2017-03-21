package com.soul.cocos2ddemo;

import android.view.MotionEvent;

import org.cocos2d.layers.CCLayer;
import org.cocos2d.nodes.CCSprite;

/**
 * * @author soul
 *
 * @项目名:Game
 * @包名: com.soul.cocos2ddemo
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/3/6 22:42
 */

public class FirstLayer extends CCLayer  {

    public FirstLayer() {
        //创建一个精灵 相对于asset目录的路径
        CCSprite ccSprite = CCSprite.sprite("z_1_01.png");
        //设置描点
        ccSprite.setAnchorPoint(0,0);//合作左下角为锚点，默认在中心
        ccSprite.setFlipX(true);//翻转
        ccSprite.setFlipY(true);//翻转
        ccSprite.setScale(0.5);//放大
        ccSprite.setOpacity(100);//透明度
        ccSprite.setPosition(240,160);

//        ccSprite.addChild();
        //布景去添加一个精灵
        this.addChild(ccSprite);

        setIsTouchEnabled(true);

    }

    @Override
    public boolean ccTouchesBegan(MotionEvent event) {
        
          System.out.println("-----------ccTouchesBegan---------");
        return super.ccTouchesBegan(event);
    }


    @Override
    public boolean ccTouchesMoved(MotionEvent event) {
        System.out.println("-----------ccTouchesMoved---------");
        return super.ccTouchesMoved(event);
    }

    @Override
    public boolean ccTouchesEnded(MotionEvent event) {
        System.out.println("-----------ccTouchesEnded---------");
        return super.ccTouchesEnded(event);
    }


    @Override
    public boolean ccTouchesCancelled(MotionEvent event) {
        System.out.println("-----------ccTouchesCancelled---------");
        return super.ccTouchesCancelled(event);
    }


}
